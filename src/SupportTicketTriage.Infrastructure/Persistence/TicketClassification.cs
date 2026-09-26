using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Infrastructure.Persistence;

/// One classification run against one ticket, with the evidence of how it was
/// produced.
///
/// A row per run rather than fields on <see cref="Ticket"/>: re-classifying
/// under a newer prompt or deployment must not erase what the system actually
/// decided at review time, which is what the provenance chain needs to answer.
public sealed class TicketClassification
{
    public Guid Id { get; private init; }
    public Guid TicketId { get; private init; }

    public TicketCategory Category { get; private init; }
    public TicketPriority Priority { get; private init; }

    /// The classifier's own estimate for the chosen category. Gate A of
    /// ADR-003, and explicitly not a calibrated probability.
    public double SelfReportedScore { get; private init; }

    /// The exact text that crossed the AI boundary, so what was sent stays
    /// auditable without re-deriving it from the raw ticket.
    public string RedactedText { get; private init; } = null!;
    public string RedactionVersion { get; private init; } = null!;
    public string ChatModel { get; private init; } = null!;
    public string PromptVersion { get; private init; } = null!;
    public DateTimeOffset CreatedAt { get; private init; }

    private TicketClassification()
    {
    }

    public static TicketClassification Create(
        Guid ticketId,
        TicketCategory category,
        TicketPriority priority,
        double selfReportedScore,
        string redactedText,
        string redactionVersion,
        string chatModel,
        string promptVersion)
    {
        if (selfReportedScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selfReportedScore), selfReportedScore, "Score must be between 0 and 1.");
        }

        return new TicketClassification
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            Category = category,
            Priority = priority,
            SelfReportedScore = selfReportedScore,
            RedactedText = redactedText,
            RedactionVersion = redactionVersion,
            ChatModel = chatModel,
            PromptVersion = promptVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
