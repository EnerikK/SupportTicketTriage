using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Infrastructure.Ai;

public sealed class TicketEmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>>? generator,
    string? embeddingModel,
    PiiRedactor redactor,
    SupportTicketTriageDbContext db,
    ILogger<TicketEmbeddingService> logger)
{
    /// Embeds a ticket and stores the vector. Returns false when the embedding
    /// could not be produced. Ingest must survive Azure OpenAI being
    /// unreachable, so this never throws for an AI-side failure.
    public async Task<bool> TryEmbedAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (generator is null || embeddingModel is null)
        {
            logger.LogInformation(
                "Azure OpenAI is not configured; ticket {TicketId} stored without an embedding.", ticket.Id);
            return false;
        }

        // Checked before the call, not after, because the call costs money.
        //
        // A vector is unique per (TicketId, EmbeddingModel). Without this
        // guard a second invocation paid for an embedding, then failed the
        // unique index on save, and the catch below reported it as "failed to
        // embed" - a wasted call reported as an outage. Ingest never hit it
        // because it only ever runs against a ticket it just created; a
        // re-drive path hits it immediately.
        //
        // Returns true because the ticket *has* an embedding under this
        // model, which is what the caller is asking about. A changed
        // deployment name is a different model and does embed again, which is
        // the backfill path the stamp exists for.
        if (await db.TicketEmbeddings.AnyAsync(
                e => e.TicketId == ticket.Id && e.EmbeddingModel == embeddingModel, ct))
        {
            return true;
        }

        // Only redacted text crosses the AI boundary.
        var redacted = redactor.Redact($"{ticket.Subject}\n\n{ticket.Body}");

        try
        {
            var vector = await generator.GenerateVectorAsync(redacted, cancellationToken: ct);

            db.TicketEmbeddings.Add(TicketEmbedding.Create(
                ticket.Id,
                redacted,
                PiiRedactor.Version,
                embeddingModel,
                new Vector(vector)));

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Absence of a TicketEmbedding row is the record that this ticket
            // still needs embedding; re-driving it is a later slice.
            logger.LogError(ex, "Failed to embed ticket {TicketId}.", ticket.Id);
            return false;
        }
    }
}
