using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Eval;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Routing;
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

    /// Reproduces a failure found the first time the pipeline ran against a
    /// real chat deployment: --reset deleted tickets directly, and the
    /// Restrict foreign key from TicketDraftSources to its source ticket
    /// refused, because a draft finally existed to be refused on behalf of.
    ///
    /// That guardrail is correct - it stops a deleted ticket silently
    /// rewriting the evidence behind an existing draft - so the reset clears
    /// reviews and drafts first rather than the constraint being relaxed.
    [Fact]
    public async Task Reset_ClearsEverythingEvenWhenADraftAndReviewExist()
    {
        var source = Ticket.Create("Duplicate charge", "Body of the duplicate charge ticket.");
        source.Resolve("The second charge was an authorisation hold.");
        _db.Tickets.Add(source);

        var ticket = Ticket.Create("Charged twice", "My card was charged twice.");
        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync();

        var retrievalSet = new[] { new RetrievedSource(source.Id, 1, 0.9) };
        var draft = TicketDraft.Create(
            ticket.Id,
            "Both charges relate to one order.",
            retrievalSet,
            CitationValidation.Validate([source.Id], retrievalSet.Select(s => s.TicketId).ToHashSet()),
            "test-chat-model",
            "v1",
            "v1");
        _db.TicketDrafts.Add(draft);
        await _db.SaveChangesAsync();

        ReviewSubmission.TryCreate(
            ReviewDecision.Approved, draft.DraftText, null, null, out var submission, out _);
        _db.TicketReviews.Add(TicketReview.Create(ticket.Id, draft.Id, submission!));
        await _db.SaveChangesAsync();

        await _runner.ResetAsync();

        Assert.False(await _db.Tickets.AnyAsync());
        Assert.False(await _db.TicketDrafts.AnyAsync());
        Assert.False(await _db.TicketReviews.AnyAsync());
        Assert.False(await _db.TicketEmbeddings.AnyAsync());
        Assert.False(await _db.Set<TicketDraftSource>().AnyAsync());
    }

    /// A run with embeddings but no chat deployment is a supported
    /// configuration, not a degraded one - it is the state this project was in
    /// for three phases. The unscored sections must come back null so the
    /// report can say "not measured" rather than printing a zero that reads as
    /// a failure.
    [Fact]
    public async Task ARunWithoutAChatDeployment_LeavesClassificationAndCitationsUnscored()
    {
        var report = await _runner.RunAsync(Dataset());

        Assert.Null(report.Classification);
        Assert.Null(report.Citations);
        Assert.Null(report.ChatModel);
        Assert.NotEmpty(report.RecallAtK);
    }

    private EvalRunner ChatRunner(FakeChatClient chat)
    {
        var retrieval = new TicketRetrievalService(_db);
        var redactor = new PiiRedactor();

        return new EvalRunner(
            _db,
            new TicketEmbeddingService(
                new LexicalEmbeddingGenerator(TicketEmbedding.Dimensions),
                LexicalEmbeddingGenerator.ModelName,
                redactor,
                _db,
                NullLogger<TicketEmbeddingService>.Instance),
            retrieval,
            LexicalEmbeddingGenerator.ModelName,
            new ChatStages(
                new TicketClassificationService(
                    chat, "fake-chat", redactor, _db, NullLogger<TicketClassificationService>.Instance),
                new TicketRoutingService(
                    _db, retrieval, RoutingThresholds.Default, NullLogger<TicketRoutingService>.Instance),
                new TicketDraftService(
                    chat, "fake-chat", redactor, _db, retrieval, NullLogger<TicketDraftService>.Instance),
                "fake-chat"));
    }

    /// Drives a classifier that is right about one category and wrong about
    /// the other, so the scored result is checkable by hand rather than being
    /// whatever the fake happened to return.
    [Fact]
    public async Task AChatRun_ScoresClassificationAgainstTheDatasetLabels()
    {
        var chat = new FakeChatClient
        {
            // Always answers "Billing". The dataset below has one Billing
            // ticket and one Technical Fault, so exactly half are correct.
            Responder = _ => """{"category": "Billing", "priority": "High", "score": 0.95}""",
        };

        var report = await ChatRunner(chat).RunAsync(TwoCategoryDataset(), skipGeneration: true);

        Assert.NotNull(report.Classification);
        Assert.Equal("fake-chat", report.ChatModel);
        Assert.Equal(0.5, report.Classification!.Accuracy, 10);
        Assert.Equal(0, report.Classification.Unclassified);

        // Classification covers every ticket, corpus and query alike - that is
        // what buys ~50 examples a category instead of ~10.
        Assert.Equal(TwoCategoryDataset().Tickets.Count, report.Classification.Total);

        // Generation was skipped, so citations stay unmeasured rather than zero.
        Assert.Null(report.Citations);
    }

    /// An unparseable response is not an error for the harness: it is a case
    /// that counts as wrong, which is what makes the accuracy figure honest
    /// about a model that refuses.
    [Fact]
    public async Task AClassifierThatAlwaysFails_ScoresZeroRatherThanThrowing()
    {
        var chat = new FakeChatClient { Responder = _ => "I'm afraid I can't do that." };

        var report = await ChatRunner(chat).RunAsync(TwoCategoryDataset(), skipGeneration: true);

        Assert.Equal(0, report.Classification!.Accuracy);
        Assert.Equal(report.Classification.Total, report.Classification.Unclassified);
    }

    /// Two tickets with different labels, one of each role, so classification
    /// covers both and retrieval still has a corpus entry to find.
    private static EvalDataset TwoCategoryDataset() =>
        new(
            "test-1.0",
            ["Billing", "Technical Fault"],
            [
                new EvalTicket(
                    "corpus-billing", TicketRole.Corpus, "Billing", "Normal",
                    "Charged twice", "My card shows two charges for one order.")
                {
                    Resolution = "The duplicate authorisation was released.",
                },
                new EvalTicket(
                    "query-technical", TicketRole.Query, "Technical Fault", "High",
                    "App crashes on launch", "The application closes immediately when I open it.")
                {
                    RelevantIds = ["corpus-billing"],
                },
            ]);
}
