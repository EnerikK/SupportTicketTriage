using SupportTicketTriage.Application.Routing;

namespace SupportTicketTriage.Infrastructure.Persistence;

/// One routing decision for one ticket, with both inputs and both thresholds
/// recorded alongside the outcome.
///
/// The thresholds are stored rather than looked up at read time because they
/// are configuration and will change: Phase 5 selects them from evaluation
/// data. Without them on the row, a decision taken today becomes unreadable
/// the moment the configured floors move, and comparing old decisions against
/// new thresholds is exactly what that tuning exercise needs.
public sealed class TicketRoutingDecision
{
    public Guid Id { get; private init; }
    public Guid TicketId { get; private init; }

    public bool IsDraftEligible { get; private init; }
    public RoutingGate FailedGates { get; private init; }

    /// Null when the ticket was never classified, as distinct from classified
    /// with a low score. Both fail Gate A; only one of them is a model
    /// declining to answer.
    public double? ClassificationScore { get; private init; }

    /// Null when retrieval returned nothing at all.
    public double? TopSimilarity { get; private init; }

    public double MinimumClassificationScore { get; private init; }
    public double MinimumSimilarity { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private TicketRoutingDecision()
    {
    }

    public static TicketRoutingDecision Create(Guid ticketId, RoutingDecision decision) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            IsDraftEligible = decision.IsDraftEligible,
            FailedGates = decision.FailedGates,
            ClassificationScore = decision.ClassificationScore,
            TopSimilarity = decision.TopSimilarity,
            MinimumClassificationScore = decision.Thresholds.MinimumClassificationScore,
            MinimumSimilarity = decision.Thresholds.MinimumSimilarity,
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
