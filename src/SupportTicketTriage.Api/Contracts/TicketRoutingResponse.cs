using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Api.Contracts;

/// The routing outcome as the review UI needs it.
///
/// `FailedGates` is a list of names rather than the raw flags value because
/// ADR-003 requires a gated ticket to say *which* gate failed — "low
/// similarity to resolved tickets", not an opaque score. Nothing here is a
/// percentage and nothing is called confidence.
public sealed record TicketRoutingResponse(
    bool IsDraftEligible,
    IReadOnlyList<string> FailedGates,
    double? ClassificationScore,
    double? TopSimilarity,
    double MinimumClassificationScore,
    double MinimumSimilarity,
    DateTimeOffset CreatedAt)
{
    public static TicketRoutingResponse FromEntity(TicketRoutingDecision decision) =>
        new(
            decision.IsDraftEligible,
            RoutingGates.Describe(decision.FailedGates),
            decision.ClassificationScore,
            decision.TopSimilarity,
            decision.MinimumClassificationScore,
            decision.MinimumSimilarity,
            decision.CreatedAt);
}
