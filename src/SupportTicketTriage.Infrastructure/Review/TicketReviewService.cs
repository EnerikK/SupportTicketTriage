using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Infrastructure.Review;

public enum ReviewOutcome
{
    Recorded,
    TicketNotFound,

    /// No draft exists for this ticket, or the one that does failed citation
    /// validation. A draft whose grounding cannot be verified is not evidence
    /// a reviewer can act on.
    NoReviewableDraft,

    /// A review already exists. Reviews are one per ticket.
    AlreadyReviewed,

    /// The submission broke a review rule — see the failure message.
    Invalid,
}

public sealed record ReviewResult(ReviewOutcome Outcome, TicketReview? Review, string? Failure);

public sealed class TicketReviewService(
    SupportTicketTriageDbContext db,
    TicketEmbeddingService embeddings,
    string? embeddingModel,
    ILogger<TicketReviewService> logger)
{
    /// Records a reviewer's decision about a ticket's draft.
    ///
    /// Approving resolves the ticket with the reviewer's final text, which is
    /// what makes it eligible for the retrieval corpus — the corpus is defined
    /// as tickets whose Resolution is not null.
    public async Task<ReviewResult> SubmitAsync(
        Guid ticketId,
        ReviewDecision decision,
        string? editedText,
        string? rejectionReason,
        CancellationToken ct = default)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        if (ticket is null)
        {
            return new ReviewResult(ReviewOutcome.TicketNotFound, null, null);
        }

        if (await db.TicketReviews.AnyAsync(r => r.TicketId == ticketId, ct))
        {
            return new ReviewResult(ReviewOutcome.AlreadyReviewed, null, null);
        }

        // A ticket seeded straight into the corpus is resolved but has no
        // draft, so it falls out here rather than reaching Ticket.Resolve and
        // throwing. That is the correct answer: there was nothing to review.
        var draft = await db.TicketDrafts
            .Where(d => d.TicketId == ticketId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (draft is null || !draft.CitationsValid)
        {
            return new ReviewResult(ReviewOutcome.NoReviewableDraft, null, null);
        }

        if (!ReviewSubmission.TryCreate(decision, draft.DraftText, editedText, rejectionReason, out var submission, out var failure))
        {
            return new ReviewResult(ReviewOutcome.Invalid, null, failure);
        }

        var accepted = submission!;
        var review = TicketReview.Create(ticketId, draft.Id, accepted);
        db.TicketReviews.Add(review);

        if (accepted.Decision == ReviewDecision.Approved)
        {
            ticket.Resolve(accepted.FinalText!);
        }

        // One SaveChanges, so the review and the resolution commit together in
        // a single transaction. The specification asks for the business
        // decision to be persisted before any re-indexing, and a change
        // tracker flush is all that takes — no outbox, no second datastore.
        await db.SaveChangesAsync(ct);

        if (accepted.Decision == ReviewDecision.Approved)
        {
            await BackfillEmbeddingIfMissingAsync(ticket, ct);
        }

        return new ReviewResult(ReviewOutcome.Recorded, review, null);
    }

    /// Approval does not re-embed anything. The stored vector is over the
    /// ticket's subject and body — the problem — and retrieval matches an
    /// incoming problem against those, then hands the matched tickets'
    /// resolutions to the model as grounding. The approved reply is payload,
    /// not key, so resolving the ticket is all that corpus entry requires.
    ///
    /// The exception is a ticket whose embedding failed at ingest. It has no
    /// vector at all, so approving it would resolve a ticket that can never be
    /// retrieved. This is the only recoverable half of the feedback loop, and
    /// it runs after the commit and tolerates failure, exactly as ingest does:
    /// an approved ticket temporarily outside the corpus can be fixed later, a
    /// lost human decision cannot.
    private async Task BackfillEmbeddingIfMissingAsync(Domain.Ticket ticket, CancellationToken ct)
    {
        if (embeddingModel is null)
        {
            return;
        }

        var alreadyEmbedded = await db.TicketEmbeddings
            .AnyAsync(e => e.TicketId == ticket.Id && e.EmbeddingModel == embeddingModel, ct);

        if (alreadyEmbedded)
        {
            return;
        }

        logger.LogInformation(
            "Approved ticket {TicketId} has no embedding under {EmbeddingModel}; backfilling.",
            ticket.Id,
            embeddingModel);

        await embeddings.TryEmbedAsync(ticket, ct);
    }
}
