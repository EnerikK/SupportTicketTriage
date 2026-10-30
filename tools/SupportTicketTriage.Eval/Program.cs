using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Baseline;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Judging;
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
        HarnessHelp.Text);
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

// A real logger, not NullLogger.
//
// Every service below used to be handed NullLogger<T>.Instance, which meant
// the harness discarded the one thing it needed when a run died: the services
// log their failures properly, and nothing was listening. A real embedding
// failure in CI surfaced as "Failed to embed 'billing-duplicate-charge'" with
// the Azure response nowhere to be found.
//
// Warning and above, so a 300-ticket run is not drowned in per-ticket
// information logs, but anything that went wrong says why.
using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Warning)
    .AddSimpleConsole(options => options.SingleLine = true));

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
        $"Loaded pricing {pricing.Version} ({pricing.Currency}, oldest rate {pricing.Models.Min(m => m.RetrievedOn):yyyy-MM-dd})"
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
    loggerFactory.CreateLogger<TicketEmbeddingService>());

// Classification and generation are scored only when a chat deployment is
// configured. Without one the harness still runs offline against the lexical
// stand-in and reports retrieval alone, with those sections marked "not
// measured" rather than scored zero.
var chatStages = BuildChatStages(db, recorder, loggerFactory);

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

// The judge is a second, stronger deployment, never the one that wrote the
// drafts: ADR-018 rests on the model not marking its own homework. Absent, the
// groundedness section reads "not measured" exactly as the chat-dependent
// sections do without a chat deployment.
var judgeEffort = ParseEffort(arguments.Value("judge-effort"));
var judge = BuildJudge(recorder, judgeEffort);

if (judge is null)
{
    Console.WriteLine(
        "No judge deployment configured: groundedness will not be measured. "
        + "Set AzureOpenAi__JudgeDeployment to a stronger deployment than the one drafting.");
}
else
{
    Console.WriteLine(
        $"Judge model: {judge.Deployment} (rubric {GroundednessPrompt.Version}, "
        + $"reasoning effort {judge.Effort}).");
}

var runner = new EvalRunner(
    db,
    embeddingService,
    new TicketRetrievalService(db),
    embedding.ModelName,
    recorder,
    chatStages,
    pricing,
    judge);

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

IReadOnlyDictionary<string, bool>? humanLabels = null;
var labelPath = arguments.Value("judge-labels");

LabelProvenance? labelProvenance = null;

if (labelPath is not null)
{
    // Before reading them. A label file with uncommitted changes cannot show
    // that it predates the verdicts it is about to validate, and that
    // ordering is the entire basis of the agreement figure.
    labelProvenance = LabelProvenance.For(labelPath);

    humanLabels = await JudgePairFile.ReadLabelsAsync(labelPath);

    Console.WriteLine(
        $"Loaded {humanLabels.Count} hand labels from {labelPath} "
        + $"(sha256 {labelProvenance.ContentSha256[..12]}, committed "
        + $"{labelProvenance.LastCommitted:yyyy-MM-dd} in {labelProvenance.LastCommit[..8]}).");
}

var report = await runner.RunAsync(
    dataset,
    skipGeneration: arguments.Has("skip-generation"),
    exportJudgePairsTo: arguments.Value("export-judge-pairs"),
    exportJudgeVerdictsTo: arguments.Value("judge-verdicts"),
    humanLabels: humanLabels,
    labelProvenance: labelProvenance);

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
/// The judge client.
///
/// Built from the same endpoint and key as the drafting client but a
/// different deployment, by handing the existing factory an options object
/// whose ChatDeployment is the judge's. That keeps the whole judge inside the
/// harness - ADR-007 puts evaluation machinery in tools/ and out of src/ -
/// and needs no change to the shipped application.
/// Rejected rather than defaulted on a typo. "lo" silently becoming the
/// provider default would put a run in the comparison table under a label it
/// did not earn.
static JudgeReasoningEffort ParseEffort(string? value) =>
    value is null
        ? JudgeReasoningEffort.Default
        : Enum.TryParse<JudgeReasoningEffort>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException(
                $"--judge-effort '{value}' is not one of: "
                + string.Join(", ", Enum.GetNames<JudgeReasoningEffort>()).ToLowerInvariant());

static GroundednessJudge? BuildJudge(AiCallRecorder recorder, JudgeReasoningEffort effort)
{
    var options = EmbeddingSelection.OptionsFromEnvironment();

    // Two names for one thing, and both are read on purpose. The
    // double-underscore form is what docker-compose binds to the AzureOpenAi
    // section, so it matches the other three; AZURE_OPENAI_CHAT_DEPLOYMENT_STRONGER
    // is what .env actually carries, and it says in the name what ADR-018
    // requires of it. Accepting only one would mean a configured judge that
    // silently does not run - the failure reads as "groundedness not
    // measured", which is indistinguishable from not having configured it.
    var deployment =
        Environment.GetEnvironmentVariable("AzureOpenAi__JudgeDeployment")
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT_STRONGER");

    if (string.IsNullOrWhiteSpace(deployment))
    {
        return null;
    }

    // Refused rather than silently honoured. A judge pointed at the drafting
    // deployment is the exact configuration ADR-018 exists to prevent, and it
    // would produce a plausible number with a structural bias nobody could
    // see from the report.
    if (deployment == options.ChatDeployment)
    {
        throw new InvalidOperationException(
            "The judge deployment is the same one that writes the drafts. "
            + "ADR-018 requires a different and stronger deployment, because a model scoring its "
            + "own output exhibits self-preference.");
    }

    var client = AzureOpenAiChatFactory.Create(new AzureOpenAiOptions
    {
        Endpoint = options.Endpoint,
        ApiKey = options.ApiKey,
        ChatDeployment = deployment,
    });

    return client is null
        ? null
        : new GroundednessJudge(
            new RecordingChatClient(client, AiStage.Judging, recorder), deployment, effort);
}

static ChatStages? BuildChatStages(
    SupportTicketTriageDbContext db, AiCallRecorder recorder, ILoggerFactory loggerFactory)
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
            loggerFactory.CreateLogger<TicketClassificationService>()),
        // The harness deliberately uses the default thresholds rather than
        // reading configuration. A baseline produced under one set of floors
        // and compared against a run under another would report a regression
        // that is really a retune, and ADR-009 stores the applied thresholds
        // on every decision precisely so that confusion is detectable.
        new TicketRoutingService(
            db,
            retrieval,
            RoutingThresholds.Default,
            loggerFactory.CreateLogger<TicketRoutingService>()),
        new TicketDraftService(
            new RecordingChatClient(chatClient, AiStage.Generation, recorder),
            options.ChatDeployment,
            redactor,
            db,
            retrieval,
            loggerFactory.CreateLogger<TicketDraftService>()),
        options.ChatDeployment!);
}
