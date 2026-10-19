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
            // Same tie-break argument as the candidate ordering below. A unique
            // index on (TicketId, EmbeddingModel) means this cannot currently
            // tie, so this is consistency rather than a fix - but an ordering
            // that is deterministic in one place and not the other is the kind
            // of asymmetry that reads as an oversight.
            .ThenByDescending(e => e.Id)
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
            // Without a second key, two candidates at exactly the same distance
            // are ordered by whatever the query plan happens to produce, so a
            // tie straddling k makes the result set differ between identical
            // requests. /similar is a public endpoint and recall@k is the
            // metric a regression gate leans on hardest; neither should depend
            // on a plan.
            //
            // Measured impact today: none. Across the 300-ticket evaluation set
            // with text-embedding-3-small there were zero exact ties in 14,400
            // query-candidate pairs - paraphrases produce distinct vectors, and
            // float4 cosine distances over 1536 dimensions did not collide. So
            // this is insurance, not a bug fix, and it is cheap enough to carry.
            // A dataset containing genuinely duplicate text would tie.
            //
            // Descending by id means the newest ticket wins a tie: ids are
            // UUIDv7 and sort by creation time, and a more recent resolution is
            // likelier to reflect current policy than an older one.
            .ThenByDescending(r => r.Id)
            .Take(k)
            .ToListAsync(ct);

        // pgvector cosine distance is 1 - cosine similarity.
        return rows
            .Select(r => new SimilarTicket(r.Id, r.Subject, r.RedactedText, r.Resolution!, 1 - r.Distance))
            .ToList();
    }
}
