using SupportTicketTriage.Application.Routing;

namespace SupportTicketTriage.Api.Contracts;

/// Names the gates a ticket failed.
///
/// ADR-003 requires a gated ticket to say *which* gate failed rather than
/// showing a number, and both the queue and the routing resource need to say
/// it the same way — hence one implementation rather than two that drift.
public static class RoutingGates
{
    public static IReadOnlyList<string> Describe(RoutingGate gates) =>
        Enum.GetValues<RoutingGate>()
            .Where(gate => gate != RoutingGate.None && gates.HasFlag(gate))
            .Select(gate => gate.ToString())
            .ToArray();
}
