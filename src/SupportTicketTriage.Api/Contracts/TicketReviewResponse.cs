using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Api.Contracts;

/// A recorded review decision.
///
/// `DraftId` is served because it is the link a reviewer — or an auditor —
/// follows back to the evidence: which draft this judged, what it was
/// grounded on, and which of those sources it cited.
public sealed record TicketReviewResponse(
    string Decision,
    string? FinalText,
    bool WasEdited,
    string? RejectionReason,
    Guid DraftId,
    DateTimeOffset CreatedAt)
{
    public static TicketReviewResponse FromEntity(TicketReview review) =>
        new(
            review.Decision.ToString(),
            review.FinalText,
            review.WasEdited,
            review.RejectionReason,
            review.TicketDraftId,
            review.CreatedAt);
}
