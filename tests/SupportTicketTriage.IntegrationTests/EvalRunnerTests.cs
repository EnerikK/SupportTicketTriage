using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using Testcontainers.PostgreSql;

namespace SupportTicketTriage.IntegrationTests;

public sealed class EvalRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    private SupportTicketTriageDbContext _db = null!;
    private EvalRunner _runner = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<SupportTicketTriageDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.UseVector())
            .Options;

        _db = new SupportTicketTriageDbContext(options);
        await _db.Database.MigrateAsync();

        var embeddings = new TicketEmbeddingService(
            new LexicalEmbeddingGenerator(TicketEmbedding.Dimensions),
            LexicalEmbeddingGenerator.ModelName,
            new PiiRedactor(),
            _db,
            NullLogger<TicketEmbeddingService>.Instance);

        _runner = new EvalRunner(
            _db, embeddings, new TicketRetrievalService(_db), LexicalEmbeddingGenerator.ModelName);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static EvalDataset Dataset() => new(
        "test-1",
        ["Billing", "Product Question"],
        [
            new EvalTicket("refund-delay", TicketRole.Corpus, "Billing", "High",
                "Refund has not arrived", "My refund was approved but the money has not reached my account.")
            {
                Resolution = "The refund was issued to an expired card and released once the bank had the reference.",
            },
            new EvalTicket("dishwasher", TicketRole.Corpus, "Product Question", "Low",
                "Cleaning instructions", "Can this go in the dishwasher or should it be washed by hand?")
            {
                Resolution = "Hand wash only; the coating degrades above sixty degrees.",
            },
            new EvalTicket("q-refund", TicketRole.Query, "Billing", "High",
                "Refund still missing", "My approved refund has not reached my account yet.")
            {
                RelevantIds = ["refund-delay"],
            },
            new EvalTicket("q-unlabelled", TicketRole.Query, "Billing", "Low", "???", "asdf !!!")
            {
                Traits = ["malformed"],
            },
        ]);

    [Fact]
    public async Task RunAsync_ScoresRetrievalAndReportsTheStandInModel()
    {
        var report = await _runner.RunAsync(Dataset());

        Assert.Equal("test-1", report.DatasetVersion);
        Assert.Equal(2, report.CorpusCount);
        Assert.Equal(2, report.QueryCount);

        // The malformed query carries no labels, so it is reported but never scored.
        Assert.Equal(1, report.ScoredQueryCount);
        Assert.Equal(1, report.UnscoredQueryCount);

        // Shared wording puts the right corpus ticket first for the lexical stand-in.
        Assert.Equal(1.0, report.RecallAtK[1]);

        Assert.Equal(LexicalEmbeddingGenerator.ModelName, report.EmbeddingModel);
        Assert.False(report.EmbeddingModelIsReal);
        Assert.Equal(1, report.TraitCoverage["malformed"]);
    }

    [Fact]
    public async Task Report_RefusesToBecomeABaselineWhenTheEmbedderIsAStandIn()
    {
        var report = await _runner.RunAsync(Dataset());

        var ex = Assert.Throws<InvalidOperationException>(report.EnsureCanBeBaseline);
        Assert.Contains("Refusing to write a baseline", ex.Message);
    }

    [Fact]
    public async Task RunAsync_SendsOnlyRedactedTextToTheEmbedder()
    {
        await _runner.RunAsync(new EvalDataset(
            "pii-1",
            ["Billing"],
            [
                new EvalTicket("pii-corpus", TicketRole.Corpus, "Billing", "High",
                    "Contact details", "Reach me at jane@example.com or 5551234567.")
                {
                    Resolution = "Contact preferences updated.",
                },
            ]));

        var stored = await _db.TicketEmbeddings.SingleAsync();

        Assert.DoesNotContain("jane@example.com", stored.RedactedText);
        Assert.DoesNotContain("5551234567", stored.RedactedText);
        Assert.Contains("[EMAIL]", stored.RedactedText);
        Assert.Contains("[PHONE]", stored.RedactedText);
    }
}
