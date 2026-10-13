using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Routing;

var arguments = ParseArguments(args);

if (arguments.TryGetValue("help", out _))
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
          --write-baseline        Write eval/baseline/retrieval-baseline.json;
                                  refused for stand-in embedders

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

if (!arguments.TryGetValue("connection", out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("--connection is required. Run with --help for usage.");
    return 1;
}

var datasetPath = arguments.GetValueOrDefault("dataset") ?? Path.Combine("eval", "dataset", "tickets.json");
var outputDirectory = arguments.GetValueOrDefault("output") ?? Path.Combine("eval", "results");

if (!File.Exists(datasetPath))
{
    Console.Error.WriteLine($"Dataset not found at {Path.GetFullPath(datasetPath)}.");
    return 1;
}

var dataset = await EvalDataset.LoadAsync(datasetPath);
Console.WriteLine($"Loaded dataset {dataset.Version}: {dataset.Tickets.Count} tickets.");

var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
    .Options;

await using var db = new SupportTicketTriageDbContext(options);
await db.Database.MigrateAsync();

var embedding = EmbeddingSelection.From(EmbeddingSelection.OptionsFromEnvironment());
Console.WriteLine($"Embedding model: {embedding.ModelName}.");

var embeddingService = new TicketEmbeddingService(
    embedding.Generator,
    embedding.ModelName,
    new PiiRedactor(),
    db,
    NullLogger<TicketEmbeddingService>.Instance);

// Classification and generation are scored only when a chat deployment is
// configured. Without one the harness still runs offline against the lexical
// stand-in and reports retrieval alone, with those sections marked "not
// measured" rather than scored zero.
var chatStages = BuildChatStages(db);

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
    db, embeddingService, new TicketRetrievalService(db), embedding.ModelName, chatStages);

if (await runner.HasExistingDataAsync())
{
    if (!arguments.ContainsKey("reset"))
    {
        Console.Error.WriteLine(
            "The target database already contains tickets. Re-run with --reset to clear them, "
            + "or point --connection at a database dedicated to evaluation.");
        return 1;
    }

    Console.WriteLine("Clearing existing tickets (--reset).");
    await runner.ResetAsync();
}

var report = await runner.RunAsync(dataset, arguments.ContainsKey("skip-generation"));

var markdownPath = Path.Combine(outputDirectory, "evaluation-report.md");
var jsonPath = Path.Combine(outputDirectory, "evaluation-result.json");
await report.WriteMarkdownAsync(markdownPath);
await report.WriteJsonAsync(jsonPath);

Console.WriteLine();
Console.WriteLine(report.ToMarkdown());
Console.WriteLine($"Wrote {Path.GetFullPath(markdownPath)} and {Path.GetFullPath(jsonPath)}.");

if (arguments.ContainsKey("write-baseline"))
{
    try
    {
        report.EnsureCanBeBaseline();

        // Deliberately not alongside the per-run reports: those are disposable
        // and gitignored, while the baseline is committed and reviewed.
        var baselinePath = Path.Combine("eval", "baseline", "retrieval-baseline.json");
        await report.WriteJsonAsync(baselinePath);
        Console.WriteLine($"Wrote {Path.GetFullPath(baselinePath)}.");
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

return 0;

static Dictionary<string, string?> ParseArguments(string[] args)
{
    var parsed = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);

        parsed[key] = hasValue ? args[++i] : null;
    }

    return parsed;
}

/// Builds the chat-dependent stages, or returns null when no chat deployment
/// is configured.
///
/// Returning null rather than throwing is deliberate: a run with embeddings
/// but no chat deployment is a supported configuration - it is the state this
/// project was in for three phases - and it still produces a real retrieval
/// number. The report marks the unscored sections rather than reporting zeros.
static ChatStages? BuildChatStages(SupportTicketTriageDbContext db)
{
    var options = EmbeddingSelection.OptionsFromEnvironment();
    var chatClient = AzureOpenAiChatFactory.Create(options);

    if (chatClient is null)
    {
        return null;
    }

    var retrieval = new TicketRetrievalService(db);
    var redactor = new PiiRedactor();

    return new ChatStages(
        new TicketClassificationService(
            chatClient,
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
            chatClient,
            options.ChatDeployment,
            redactor,
            db,
            retrieval,
            NullLogger<TicketDraftService>.Instance),
        options.ChatDeployment!);
}
