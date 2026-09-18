using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

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
          --write-baseline        Write eval/baseline/retrieval-baseline.json;
                                  refused for stand-in embedders
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
    Console.Error.WriteLine($"Dataset not found at {datasetPath}.");
    return 1;
}

var dataset = await EvalDataset.LoadAsync(datasetPath);
Console.WriteLine($"Loaded dataset {dataset.Version}: {dataset.Tickets.Count} tickets.");

var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
    .Options;

await using var db = new SupportTicketTriageDbContext(options);
await db.Database.MigrateAsync();

var generator = new LexicalEmbeddingGenerator(TicketEmbedding.Dimensions);
var embeddingService = new TicketEmbeddingService(
    generator,
    LexicalEmbeddingGenerator.ModelName,
    new PiiRedactor(),
    db,
    NullLogger<TicketEmbeddingService>.Instance);

var runner = new EvalRunner(db, embeddingService, new TicketRetrievalService(db), LexicalEmbeddingGenerator.ModelName);

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

var report = await runner.RunAsync(dataset);

var markdownPath = Path.Combine(outputDirectory, "retrieval-report.md");
var jsonPath = Path.Combine(outputDirectory, "retrieval-result.json");
await report.WriteMarkdownAsync(markdownPath);
await report.WriteJsonAsync(jsonPath);

Console.WriteLine();
Console.WriteLine(report.ToMarkdown());
Console.WriteLine($"Wrote {markdownPath} and {jsonPath}.");

if (arguments.ContainsKey("write-baseline"))
{
    try
    {
        report.EnsureCanBeBaseline();

        // Deliberately not alongside the per-run reports: those are disposable
        // and gitignored, while the baseline is committed and reviewed.
        var baselinePath = Path.Combine("eval", "baseline", "retrieval-baseline.json");
        await report.WriteJsonAsync(baselinePath);
        Console.WriteLine($"Wrote {baselinePath}.");
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
