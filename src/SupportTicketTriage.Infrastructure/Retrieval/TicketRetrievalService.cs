using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Infrastructure.Retrieval;

public sealed record SimilarTicket(
    Guid TicketId,
    string Subject,
    string RedactedText,
    string Resolution,
    double Similarity);

public sealed class TicketRetrievalService(SupportTicketTriageDbContext db)
{
    public const int DefaultK = 5;

    /// Finds resolved tickets whose embeddings are nearest the given ticket's.
    /// Exact search, no approximate index: the corpus is small, and an ANN
    /// index would trade away the recall the evaluation harness measures.
    public async Task<IReadOnlyList<SimilarTicket>> FindSimilarAsync(
        Guid ticketId,
        int k = DefaultK,
        CancellationToken ct = default)
    {
        var query = await db.TicketEmbeddings
            .Where(e => e.TicketId == ticketId)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (query is null)
        {
            return [];
        }

        var rows = await db.TicketEmbeddings
            .Where(e => e.TicketId != ticketId)
            // Vectors are only comparable within the same deployment, which is
            // what the model stamp on each row exists to enforce.
            .Where(e => e.EmbeddingModel == query.EmbeddingModel)
            .Join(
                db.Tickets.Where(t => t.Resolution != null),
                e => e.TicketId,
                t => t.Id,
                (e, t) => new
                {
                    t.Id,
                    t.Subject,
                    e.RedactedText,
                    t.Resolution,
                    Distance = e.Embedding.CosineDistance(query.Embedding),
                })
            .OrderBy(r => r.Distance)
            .Take(k)
            .ToListAsync(ct);

        // pgvector cosine distance is 1 - cosine similarity.
        return rows
            .Select(r => new SimilarTicket(r.Id, r.Subject, r.RedactedText, r.Resolution!, 1 - r.Distance))
            .ToList();
    }
}
