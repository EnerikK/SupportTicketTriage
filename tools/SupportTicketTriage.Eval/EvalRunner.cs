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
        var scoredRetrieval = await EvaluateRetrievalAsync(dataset, ticketIds, ct);
        var retrievalCases = scoredRetrieval.Select(s => s.Case).ToList();

        ClassificationScore? classification = null;
        CitationScore? citations = null;
        var calibration = new List<GateCalibration>
        {
            CalibrateRetrievalGate(scoredRetrieval),
        };

        if (chat is not null)
        {
            var scoredClassification = await ClassifyAsync(dataset, ticketIds, ct);
            classification = ClassificationMetrics.Summarise(
                scoredClassification.Select(s => s.Case).ToList());
            calibration.Insert(0, CalibrateClassificationGate(scoredClassification));

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
            Calibration: calibration,
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

    private sealed record ScoredRetrieval(RetrievalCase Case, double? TopSimilarity);

    private sealed record ScoredClassification(ClassificationCase Case, double? SelfReportedScore);

    private async Task<List<ScoredRetrieval>> EvaluateRetrievalAsync(
        EvalDataset dataset,
        IReadOnlyDictionary<string, Guid> ticketIds,
        CancellationToken ct)
    {
        var datasetIdByTicketId = ticketIds.ToDictionary(kv => kv.Value, kv => kv.Key);
        var maxK = KValues.Max();
        var cases = new List<ScoredRetrieval>();

        foreach (var query in dataset.Queries)
        {
            var results = await retrieval.FindSimilarAsync(ticketIds[query.Id], maxK, ct);

            cases.Add(new ScoredRetrieval(
                new RetrievalCase(
                    query.Id,
                    query.RelevantIds,
                    results.Select(r => datasetIdByTicketId[r.TicketId]).ToList()),
                // What Gate B compares against its floor. Null when nothing was
                // retrieved at all, which is an absent input rather than a
                // similarity of zero.
                results.Count == 0 ? null : results[0].Similarity));
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
    private async Task<List<ScoredClassification>> ClassifyAsync(
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
            .ToDictionaryAsync(
                c => c.TicketId,
                c => new { Label = c.Category.ToLabel(), c.SelfReportedScore },
                ct);

        return dataset.Tickets
            .Select(entry =>
            {
                var row = stored.GetValueOrDefault(ticketIds[entry.Id]);

                return new ScoredClassification(
                    new ClassificationCase(entry.Id, entry.Category, row?.Label),
                    // Null when the ticket was never classified: Gate A has no
                    // input to compare, which is a failed gate rather than a
                    // score of zero.
                    row?.SelfReportedScore);
            })
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


    /// Gate A: does the classifier's self-reported score predict whether it
    /// was right?
    ///
    /// Success is "classified correctly", which is what the gate is nominally
    /// protecting - a ticket the classifier got wrong should ideally have been
    /// sent to a human instead.
    private static GateCalibration CalibrateClassificationGate(
        IReadOnlyCollection<ScoredClassification> scored)
    {
        var cases = scored
            .Select(s => new CalibrationCase(s.Case.QueryId, s.SelfReportedScore, s.Case.IsCorrect))
            .ToList();

        return new GateCalibration(
            Gate: "A - classification score",
            ScoreName: "classifier self-reported score",
            SuccessName: "classified correctly",
            Separation: CalibrationMetrics.Separation(cases),
            AbsentScore: CalibrationMetrics.AbsentScoreCount(cases),
            Floors: CalibrationMetrics.AtEachFloor(cases),
            // Every ticket carries a category label, so classification
            // success is always defined.
            UndefinedOutcomeScores: []);
    }

    /// Gate B: does top-1 similarity predict whether retrieval actually found
    /// something worth grounding on?
    ///
    /// Success is "at least one labelled-relevant ticket appeared in the top
    /// k", using the same k the draft is grounded on - the gate exists to stop
    /// a draft being attempted when nothing useful came back, so the question
    /// is about the set the model would have been given.
    ///
    /// Queries with no labels are excluded rather than counted as failures.
    /// A malformed ticket legitimately has no correct answer, so "retrieval
    /// succeeded" is undefined for it - the same reason RecallAt returns null.
    /// They are exactly the cases the gate should catch, but they are not
    /// evidence about where the floor belongs.
    private static GateCalibration CalibrateRetrievalGate(
        IReadOnlyCollection<ScoredRetrieval> scored)
    {
        // The k a draft is actually grounded on, not KValues.Max(). Gate B
        // exists to stop a draft being attempted when the set the model would
        // receive holds nothing useful, so the question has to be asked about
        // that set. Calibrating against the top 10 measures a retrieval depth
        // no draft ever sees, and at 10 almost every query succeeds - which
        // leaves no variance to calibrate against at all.
        var k = TicketRetrievalService.DefaultK;

        var cases = scored
            .Where(s => s.Case.RelevantIds.Count > 0)
            .Select(s => new CalibrationCase(
                s.Case.QueryId,
                s.TopSimilarity,
                s.Case.RetrievedIds.Take(k).Intersect(s.Case.RelevantIds).Any()))
            .ToList();

        return new GateCalibration(
            Gate: "B - retrieval similarity",
            ScoreName: "top-1 cosine similarity",
            SuccessName: $"a relevant ticket in the top {k}",
            Separation: CalibrationMetrics.Separation(cases),
            AbsentScore: CalibrationMetrics.AbsentScoreCount(cases),
            Floors: CalibrationMetrics.AtEachFloor(cases),
            // The unlabelled queries. Excluded from the table above because
            // success is undefined for them, but reported here because a
            // floor above all of them refuses every query that has no
            // answer - which is the clearest evidence there is for where
            // this floor belongs.
            UndefinedOutcomeScores: scored
                .Where(s => s.Case.RelevantIds.Count == 0 && s.TopSimilarity is not null)
                .Select(s => s.TopSimilarity!.Value)
                .OrderBy(v => v)
                .ToList());
    }
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
