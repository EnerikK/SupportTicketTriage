using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Routing;

namespace SupportTicketTriage.Eval;

/// The chat-dependent half of the pipeline.
///
/// All of it arrives together or not at all. Classification and generation
/// both need a chat deployment, and routing without a classification would
/// fail Gate A for every ticket — so a run with routing but no classifier
/// would report that generation was never attempted and call it a result.
/// One nullable group rather than three independently-nullable services makes
/// that impossible to assemble wrongly.
public sealed record ChatStages(
    TicketClassificationService Classification,
    TicketRoutingService Routing,
    TicketDraftService Drafts,
    string ChatModel);

public sealed class EvalRunner(
    SupportTicketTriageDbContext db,
    TicketEmbeddingService embeddings,
    TicketRetrievalService retrieval,
    string embeddingModel,
    ChatStages? chat = null)
{
    public static readonly int[] KValues = [1, 3, 5, 10];

    /// Generation can be skipped independently of classification, but not the
    /// reverse: the routing gates read what the classifier produced, so a
    /// generation pass without one would refuse every ticket at Gate A and
    /// report that as a citation result.
    public async Task<EvaluationReport> RunAsync(
        EvalDataset dataset,
        bool skipGeneration = false,
        CancellationToken ct = default)
    {
        var ticketIds = await SeedAsync(dataset, ct);
        var retrievalCases = await EvaluateRetrievalAsync(dataset, ticketIds, ct);

        ClassificationScore? classification = null;
        CitationScore? citations = null;

        if (chat is not null)
        {
            classification = ClassificationMetrics.Summarise(await ClassifyAsync(dataset, ticketIds, ct));

            if (!skipGeneration)
            {
                citations = CitationMetrics.Summarise(await GenerateAsync(dataset, ticketIds, ct));
            }
        }

        return new EvaluationReport(
            DatasetVersion: dataset.Version,
            EmbeddingModel: embeddingModel,
            EmbeddingModelIsReal: EvaluationReport.IsRealEmbeddingModel(embeddingModel),
            ChatModel: chat?.ChatModel,
            GeneratedAt: DateTimeOffset.UtcNow,
            CorpusCount: dataset.Corpus.Count(),
            QueryCount: dataset.Queries.Count(),
            ScoredQueryCount: RetrievalMetrics.ScoredCaseCount(retrievalCases),
            UnscoredQueryCount: RetrievalMetrics.UnscoredCaseCount(retrievalCases),
            RecallAtK: KValues.ToDictionary(k => k, k => RetrievalMetrics.MeanRecallAt(retrievalCases, k)),
            Classification: classification,
            Citations: citations,
            TraitCoverage: dataset.Tickets
                .SelectMany(t => t.Traits)
                .GroupBy(trait => trait)
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    /// Seeds through the application's own code paths - the domain factory, the
    /// redactor and the embedding service - so the evaluation measures what the
    /// application actually stores, not a shortcut built for the harness.
    private async Task<Dictionary<string, Guid>> SeedAsync(EvalDataset dataset, CancellationToken ct)
    {
        var ids = new Dictionary<string, Guid>(dataset.Tickets.Count);

        foreach (var entry in dataset.Tickets)
        {
            var ticket = Ticket.Create(entry.Subject, entry.Body);

            if (entry.Role == TicketRole.Corpus)
            {
                ticket.Resolve(entry.Resolution!);
            }

            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(ct);

            if (!await embeddings.TryEmbedAsync(ticket, ct))
            {
                throw new InvalidOperationException(
                    $"Failed to embed '{entry.Id}'. The evaluation cannot proceed with missing vectors.");
            }

            ids[entry.Id] = ticket.Id;
        }

        return ids;
    }

    private async Task<List<RetrievalCase>> EvaluateRetrievalAsync(
        EvalDataset dataset,
        IReadOnlyDictionary<string, Guid> ticketIds,
        CancellationToken ct)
    {
        var datasetIdByTicketId = ticketIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var maxK = KValues.Max();
        var cases = new List<RetrievalCase>();

        foreach (var query in dataset.Queries)
        {
            var results = await retrieval.FindSimilarAsync(ticketIds[query.Id], maxK, ct);

            cases.Add(new RetrievalCase(
                query.Id,
                query.RelevantIds,
                results.Select(r => datasetIdByTicketId[r.TicketId]).ToList()));
        }

        return cases;
    }

    /// Classifies **every** ticket, corpus and query alike, not just the 60
    /// queries.
    ///
    /// The dataset labels all 300 and is balanced at 50 per category, so this
    /// scores macro-F1 over ~50 examples per class instead of ~10. That is the
    /// same sample-size argument the retrieval dataset already forced: at ten
    /// examples a class, one misclassification moves per-class F1 by enough
    /// that a regression gate would fire on noise. Corpus tickets are the same
    /// kind of text the classifier sees at ingest, so classifying them tests
    /// the same thing - they are only "history" by virtue of carrying a
    /// resolution.
    ///
    /// The cost is one chat call per ticket, sequentially.
    private async Task<List<ClassificationCase>> ClassifyAsync(
        EvalDataset dataset,
        IReadOnlyDictionary<string, Guid> ticketIds,
        CancellationToken ct)
    {
        foreach (var entry in dataset.Tickets)
        {
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketIds[entry.Id], ct);

            // Failure is not fatal here, and must not be: an unclassified
            // ticket is a case that counts as wrong, which is exactly what the
            // metric is meant to capture.
            await chat!.Classification.TryClassifyAsync(ticket, ct);
        }

        var stored = await db.TicketClassifications
            .GroupBy(c => c.TicketId)
            .Select(g => g.OrderByDescending(c => c.CreatedAt).First())
            .ToDictionaryAsync(c => c.TicketId, c => c.Category.ToLabel(), ct);

        return dataset.Tickets
            .Select(entry => new ClassificationCase(
                entry.Id,
                entry.Category,
                stored.GetValueOrDefault(ticketIds[entry.Id])))
            .ToList();
    }

    /// Routes and then drafts for the query tickets.
    ///
    /// Routing runs first because generation refuses a ticket whose gates did
    /// not clear, and the gates read what classification produced. Only
    /// queries are drafted: corpus tickets already carry a resolution, so a
    /// draft for one would be answering a question that is not being asked.
    private async Task<List<CitationCase>> GenerateAsync(
        EvalDataset dataset,
        IReadOnlyDictionary<string, Guid> ticketIds,
        CancellationToken ct)
    {
        var cases = new List<CitationCase>();

        foreach (var query in dataset.Queries)
        {
            var ticket = await db.Tickets.SingleAsync(t => t.Id == ticketIds[query.Id], ct);

            await chat!.Routing.TryEvaluateAsync(ticket, ct);
            var result = await chat.Drafts.GenerateAsync(ticket, ct);

            cases.Add(ToCitationCase(query.Id, result));
        }

        return cases;
    }

    private static CitationCase ToCitationCase(string queryId, DraftGenerationResult result) =>
        result.Outcome switch
        {
            DraftOutcome.Created or DraftOutcome.AlreadyExists when result.Draft!.CitationsValid =>
                new CitationCase(
                    queryId,
                    DraftOutcomeKind.Valid,
                    result.Draft.Sources.Count(s => s.WasCited),
                    0),

            DraftOutcome.Created or DraftOutcome.AlreadyExists =>
                new CitationCase(
                    queryId,
                    DraftOutcomeKind.InvalidCitations,
                    result.Draft!.Sources.Count(s => s.WasCited) + result.Draft.InvalidCitationCount,
                    result.Draft.InvalidCitationCount),

            DraftOutcome.NotEligible => new CitationCase(queryId, DraftOutcomeKind.NotEligible, 0, 0),

            // A run that reached here with no chat deployment is a harness
            // wiring error, not a result worth recording.
            DraftOutcome.NotConfigured => throw new InvalidOperationException(
                $"Generation reported no chat deployment while evaluating '{queryId}'."),

            _ => new CitationCase(queryId, DraftOutcomeKind.Failed, 0, 0),
        };

    public async Task<bool> HasExistingDataAsync(CancellationToken ct = default) =>
        await db.Tickets.AnyAsync(ct);

    /// The order here is load-bearing, and the database enforces it.
    ///
    /// Reviews first: a review points at the draft it judged with NoAction,
    /// so deleting drafts underneath one is refused. Drafts next, which
    /// cascades to their source rows - and those are what actually block a
    /// bare "delete the tickets": TicketDraftSources references the source
    /// ticket with Restrict, deliberately, so that removing a historical
    /// ticket can never silently rewrite the evidence an existing draft was
    /// grounded on. Classifications, routing decisions and embeddings all
    /// cascade from the ticket and need no help.
    ///
    /// This only ever mattered once a draft existed, which is why it went
    /// unnoticed until the pipeline was first run against a real chat
    /// deployment.
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await db.TicketReviews.ExecuteDeleteAsync(ct);
        await db.TicketDrafts.ExecuteDeleteAsync(ct);
        await db.TicketEmbeddings.ExecuteDeleteAsync(ct);
        await db.Tickets.ExecuteDeleteAsync(ct);
    }
}
