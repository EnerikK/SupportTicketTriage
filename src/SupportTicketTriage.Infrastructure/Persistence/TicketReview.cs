using SupportTicketTriage.Application.Review;

namespace SupportTicketTriage.Infrastructure.Persistence;

/// One human decision about one draft, and the text that decision produced.
///
/// This is the row that makes the feedback loop traceable: it names the draft
/// it judged, so the chain from ticket through retrieval set, draft and
/// citations to the approved resolution is a join rather than a guess.
///
/// There is exactly one per ticket, enforced by a unique index. ADR-008
/// deliberately left `TicketClassifications` without one because a second
/// classification is redundant rather than dangerous; a second review would
/// be a silent reversal of a recorded human decision, which is the opposite.
public sealed class TicketReview
{
    public Guid Id { get; private init; }
    public Guid TicketId { get; private init; }

    /// The draft that was reviewed. Required: there is nothing to approve or
    /// reject without one.
    public Guid TicketDraftId { get; private init; }

    public ReviewDecision Decision { get; private init; }

    /// The reviewer's final text, which becomes the ticket's resolution.
    /// Null on a rejection, because a rejected draft resolves nothing.
    public string? FinalText { get; private init; }

    /// Whether the reviewer changed the draft before approving. Decided once,
    /// at the review, from two values that never change afterwards.
    public bool WasEdited { get; private init; }

    /// Null unless rejected.
    public string? RejectionReason { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    private TicketReview()
    {
    }

    public static TicketReview Create(Guid ticketId, Guid draftId, ReviewSubmission submission) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            TicketDraftId = draftId,
            Decision = submission.Decision,
            FinalText = submission.FinalText,
            WasEdited = submission.WasEdited,
            RejectionReason = submission.RejectionReason,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
