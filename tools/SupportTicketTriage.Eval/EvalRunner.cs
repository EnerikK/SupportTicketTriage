using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Eval;

public sealed class EvalRunner(
    SupportTicketTriageDbContext db,
    TicketEmbeddingService embeddings,
    TicketRetrievalService retrieval,
    string embeddingModel)
{
    public static readonly int[] KValues = [1, 3, 5, 10];

    public async Task<RetrievalReport> RunAsync(EvalDataset dataset, CancellationToken ct = default)
    {
        var ticketIds = await SeedAsync(dataset, ct);
        var cases = await EvaluateAsync(dataset, ticketIds, ct);

        return new RetrievalReport(
            DatasetVersion: dataset.Version,
            EmbeddingModel: embeddingModel,
            EmbeddingModelIsReal: RetrievalReport.IsRealEmbeddingModel(embeddingModel),
            GeneratedAt: DateTimeOffset.UtcNow,
            CorpusCount: dataset.Corpus.Count(),
            QueryCount: dataset.Queries.Count(),
            ScoredQueryCount: RetrievalMetrics.ScoredCaseCount(cases),
            UnscoredQueryCount: RetrievalMetrics.UnscoredCaseCount(cases),
            RecallAtK: KValues.ToDictionary(k => k, k => RetrievalMetrics.MeanRecallAt(cases, k)),
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

    private async Task<List<RetrievalCase>> EvaluateAsync(
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

    public async Task<bool> HasExistingDataAsync(CancellationToken ct = default) =>
        await db.Tickets.AnyAsync(ct);

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await db.TicketEmbeddings.ExecuteDeleteAsync(ct);
        await db.Tickets.ExecuteDeleteAsync(ct);
    }
}
