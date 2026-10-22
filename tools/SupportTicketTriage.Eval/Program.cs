using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Baseline;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Pricing;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Routing;

HarnessArguments arguments;

try
{
    arguments = HarnessArguments.Parse(args);
}
catch (ArgumentException error)
{
    // Exit before touching anything. An unrecognised flag means the caller
    // asked for something this harness does not do, and carrying on would
    // run a different evaluation from the one requested - silently, and
    // successfully, which is how a regression gate comes to guard nothing.
    Console.Error.WriteLine(error.Message);
    return 1;
}

if (arguments.Has("help"))
{
    Console.WriteLine(
        """
        Retrieval evaluation harness.

          --connection <string>   PostgreSQL connection string (required)
          --dataset <path>        Dataset JSON (default: eval/dataset/tickets.json)
          --output <dir>          Report directory (default: eval/results)
          --reset                 Delete existing tickets before seeding
          --skip-generation       Score classification but not drafts (saves the
                                  generation and citation calls)
          --pricing <path>        Token pricing (default: eval/pricing/model-pricing.json);
                                  costs are reported as not scored without it
          --compare-baseline [p]  Compare this run against a baseline and exit
                                  non-zero on regression beyond tolerance
                                  (default: eval/baseline/evaluation-baseline.json)
          --write-logic-baseline  Write eval/baseline/lexical-logic-baseline.json
                                  from a stand-in run; guards retrieval logic,
                                  not retrieval quality
          --write-baseline        Write eval/baseline/evaluation-baseline.json;
                                  refused for stand-in embedders and for costs
                                  computed from unverified pricing

        Deployments are read from the environment, not from arguments, so the key
        stays out of shell history.

          AzureOpenAi__Endpoint
          AzureOpenAi__ApiKey
          AzureOpenAi__EmbeddingDeployment   retrieval; without it the run falls
                                             back to the lexical stand-in, which
                                             cannot produce a baseline
          AzureOpenAi__ChatDeployment        classification and citation validity;
                                             without it those are reported as not
                                             measured rather than scored zero
        """);
    return 0;
}

var connectionString = arguments.Value("connection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("--connection is required. Run with --help for usage.");
    return 1;
}

var datasetPath = arguments.Value("dataset") ?? Path.Combine("eval", "dataset", "tickets.json");
var outputDirectory = arguments.Value("output") ?? Path.Combine("eval", "results");

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine($"Dataset not found at {Path.GetFullPath(datasetPath)}.");
    return 1;
}

var dataset = await EvalDataset.LoadAsync(datasetPath);
Console.WriteLine($"Loaded dataset {dataset.Version}: {dataset.Tickets.Count} tickets.");

// Absent rather than fatal: a run can legitimately measure latency and skip
// costing, and the report says "not scored" instead of reporting a run as
// free. A file that exists but is malformed does throw - that is a mistake,
// not a choice.
var pricingPath = arguments.Value("pricing")
    ?? Path.Combine("eval", "pricing", "model-pricing.json");
ModelPricing? pricing = null;

if (File.Exists(pricingPath))
{
    pricing = await ModelPricing.LoadAsync(pricingPath);
    Console.WriteLine(
        $"Loaded pricing {pricing.Version} ({pricing.Currency}, retrieved {pricing.RetrievedOn:yyyy-MM-dd})"
        + (pricing.Verified ? "." : " - UNVERIFIED, costs are a development aid only."));
}
else
{
    Console.WriteLine($"No pricing file at {pricingPath}: costs will not be scored.");
}

var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
    .Options;

await using var db = new SupportTicketTriageDbContext(options);
await db.Database.MigrateAsync();

var embedding = EmbeddingSelection.From(EmbeddingSelection.OptionsFromEnvironment());
Console.WriteLine($"Embedding model: {embedding.ModelName}.");

// One recorder for the whole run. Every decorator writes into it and the
// runner reads it once at the end, which keeps "what did this cost" in a
// single place rather than threaded back through four return types.
var recorder = new AiCallRecorder();

var embeddingService = new TicketEmbeddingService(
    new RecordingEmbeddingGenerator(embedding.Generator, recorder),
    embedding.ModelName,
    new PiiRedactor(),
    db,
    NullLogger<TicketEmbeddingService>.Instance);

// Classification and generation are scored only when a chat deployment is
// configured. Without one the harness still runs offline against the lexical
// stand-in and reports retrieval alone, with those sections marked "not
// measured" rather than scored zero.
var chatStages = BuildChatStages(db, recorder);

if (chatStages is null)
{
    Console.WriteLine(
        "No chat deployment configured: scoring retrieval only. "
        + "Set AzureOpenAi__ChatDeployment (with endpoint and key) to score classification and citations.");
}
else
{
    Console.WriteLine($"Chat model: {chatStages.ChatModel}.");
}

var runner = new EvalRunner(
    db,
    embeddingService,
    new TicketRetrievalService(db),
    embedding.ModelName,
    recorder,
    chatStages,
    pricing);

if (await runner.HasExistingDataAsync())
{
    if (!arguments.Has("reset"))
    {
        Console.Error.WriteLine(
            "The target database already contains tickets. Re-run with --reset to clear them, "
            + "or point --connection at a database dedicated to evaluation.");
        return 1;
    }

    Console.WriteLine("Clearing existing tickets (--reset).");
    await runner.ResetAsync();
}

var report = await runner.RunAsync(dataset, arguments.Has("skip-generation"));

var markdownPath = Path.Combine(outputDirectory, "evaluation-report.md");
var jsonPath = Path.Combine(outputDirectory, "evaluation-result.json");
await report.WriteMarkdownAsync(markdownPath);
await report.WriteJsonAsync(jsonPath);

Console.WriteLine();
Console.WriteLine(report.ToMarkdown());
Console.WriteLine($"Wrote {Path.GetFullPath(markdownPath)} and {Path.GetFullPath(jsonPath)}.");

if (arguments.Has("compare-baseline"))
{
    var comparisonPath = arguments.Value("compare-baseline") ?? DefaultBaselinePath();

    if (!File.Exists(comparisonPath))
    {
        Console.Error.WriteLine(
            $"No baseline at {Path.GetFullPath(comparisonPath)}. "
            + "Write one with --write-baseline before gating against it.");
        return 1;
    }

    EvaluationBaseline baseline;

    try
    {
        baseline = await EvaluationBaseline.LoadAsync(comparisonPath);
    }
    catch (InvalidOperationException error)
    {
        // A malformed or out-of-date baseline is a failure, not a pass. The
        // previous baseline rotted unnoticed precisely because nothing ever
        // tried to read it.
        Console.Error.WriteLine(error.Message);
        return 1;
    }

    var comparison = BaselineComparison.Compare(baseline, report);

    Console.WriteLine();
    Console.WriteLine(comparison.ToMarkdown());

    if (!comparison.Passed)
    {
        Console.Error.WriteLine(comparison.Comparable
            ? "Regression gate failed."
            : "Regression gate could not run: this baseline does not describe this run.");
        return 1;
    }
}

if (arguments.Has("write-logic-baseline"))
{
    try
    {
        report.EnsureCanBeLogicBaseline();

        var logicPath = LogicBaselinePath();
        await EvaluationBaseline.From(report).WriteAsync(logicPath);
        Console.WriteLine($"Wrote {Path.GetFullPath(logicPath)}.");
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if (arguments.Has("write-baseline"))
{
    try
    {
        report.EnsureCanBeBaseline();

        // Deliberately not alongside the per-run reports: those are disposable
        // and gitignored, while the baseline is committed and reviewed.
        //
        // And deliberately not the report itself. The previous baseline was a
        // full report dump and it rotted - six properties were added to the
        // report afterwards, and the committed file can no longer be read back
        // into the record it came from. A projection of only the gated fields
        // cannot rot that way, and it makes the gate's scope reviewable in the
        // file rather than only in the comparison code.
        var baselinePath = DefaultBaselinePath();
        await EvaluationBaseline.From(report).WriteAsync(baselinePath);
        Console.WriteLine($"Wrote {Path.GetFullPath(baselinePath)}.");
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

return 0;

/// Deliberately not alongside the per-run reports: those are disposable and
/// gitignored, while the baseline is committed and reviewed.
static string DefaultBaselinePath() => Path.Combine("eval", "baseline", "evaluation-baseline.json");

/// Named so it cannot be mistaken for the real one at a glance in a diff or a
/// CI log. The provenance check does the actual work of keeping them apart -
/// a real run compared against this file mismatches on embedding model and is
/// reported as inapplicable rather than failed - but the name is the first
/// line of defence and costs nothing.
static string LogicBaselinePath() => Path.Combine("eval", "baseline", "lexical-logic-baseline.json");

/// Builds the chat-dependent stages, or returns null when no chat deployment
/// is configured.
///
/// Returning null rather than throwing is deliberate: a run with embeddings
/// but no chat deployment is a supported configuration - it is the state this
/// project was in for three phases - and it still produces a real retrieval
/// number. The report marks the unscored sections rather than reporting zeros.
static ChatStages? BuildChatStages(SupportTicketTriageDbContext db, AiCallRecorder recorder)
{
    var options = EmbeddingSelection.OptionsFromEnvironment();
    var chatClient = AzureOpenAiChatFactory.Create(options);

    if (chatClient is null)
    {
        return null;
    }

    var retrieval = new TicketRetrievalService(db);
    var redactor = new PiiRedactor();

    // Wrapped twice, once per stage. One IChatClient serves both services, so
    // a single decorator could not say whether a call was a classification or
    // a draft - and those have very different latencies and token counts, so
    // blending them would hide the expensive one behind 300 cheap ones.
    return new ChatStages(
        new TicketClassificationService(
            new RecordingChatClient(chatClient, AiStage.Classification, recorder),
            options.ChatDeployment,
            redactor,
            db,
            NullLogger<TicketClassificationService>.Instance),
        // The harness deliberately uses the default thresholds rather than
        // reading configuration. A baseline produced under one set of floors
        // and compared against a run under another would report a regression
        // that is really a retune, and ADR-009 stores the applied thresholds
        // on every decision precisely so that confusion is detectable.
        new TicketRoutingService(
            db,
            retrieval,
            RoutingThresholds.Default,
            NullLogger<TicketRoutingService>.Instance),
        new TicketDraftService(
            new RecordingChatClient(chatClient, AiStage.Generation, recorder),
            options.ChatDeployment,
            redactor,
            db,
            retrieval,
            NullLogger<TicketDraftService>.Instance),
        options.ChatDeployment!);
}
