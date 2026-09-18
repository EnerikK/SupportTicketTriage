using Pgvector;

namespace SupportTicketTriage.Infrastructure.Persistence;

public sealed class TicketEmbedding
{
    // pgvector needs a fixed dimension at DDL time to build an index, so the
    // column is pinned to text-embedding-3-small's size. Moving to a model with
    // a different width is a migration plus a backfill, not a config change.
    public const int Dimensions = 1536;

    public Guid Id { get; private init; }
    public Guid TicketId { get; private init; }

    // The exact text that crossed the AI boundary, kept so that what was sent
    // is auditable and so retrieval can ground on redacted content directly.
    public string RedactedText { get; private init; } = null!;
    public string RedactionVersion { get; private init; } = null!;
    public string EmbeddingModel { get; private init; } = null!;
    public Vector Embedding { get; private init; } = null!;
    public DateTimeOffset CreatedAt { get; private init; }

    private TicketEmbedding()
    {
    }

    public static TicketEmbedding Create(
        Guid ticketId,
        string redactedText,
        string redactionVersion,
        string embeddingModel,
        Vector embedding)
    {
        if (embedding.Memory.Length != Dimensions)
        {
            throw new ArgumentException(
                $"Embedding must have {Dimensions} dimensions, got {embedding.Memory.Length}.",
                nameof(embedding));
        }

        return new TicketEmbedding
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            RedactedText = redactedText,
            RedactionVersion = redactionVersion,
            EmbeddingModel = embeddingModel,
            Embedding = embedding,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
