using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Api.Contracts;

/// How far a ticket's draft has got.
///
/// Three-valued rather than a boolean: "nothing was generated" and "something
/// was generated but cited evidence it was never shown" are different things
/// to a reviewer, and only the second is a failure worth chasing. ADR-010
/// withholds the content of the second, not its existence.
public static class DraftStates
{
    public const string None = "None";
    public const string Failed = "Failed";
    public const string Ready = "Ready";

    public static string From(bool? citationsValid) => citationsValid switch
    {
        null => None,
        false => Failed,
        true => Ready,
    };
}

/// One row of the review queue.
///
/// Deliberately not <see cref="TicketResponse"/>: the queue has no use for
/// the ticket body, and the detail view has no use for a flattened triage
/// summary when it has richer resources per signal.
///
/// No score appears here. ADR-003 forbids presenting the routing inputs as a
/// confidence percentage, so the queue says which gate failed and leaves the
/// numbers to the routing resource.
public sealed record TicketQueueItemResponse(
    Guid Id,
    string Subject,
    DateTimeOffset CreatedAt,
    string? Category,
    string? Priority,
    bool? IsDraftEligible,
    IReadOnlyList<string> FailedGates,
    string DraftState,
    string? ReviewDecision,
    bool IsResolved)
{
    public static TicketQueueItemResponse Create(
        Guid id,
        string subject,
        DateTimeOffset createdAt,
        bool isResolved,
        TicketCategory? category,
        TicketPriority? priority,
        bool? isDraftEligible,
        RoutingGate failedGates,
        bool? draftCitationsValid,
        ReviewDecision? reviewDecision) =>
        new(
            id,
            subject,
            createdAt,
            // The wire label, matching the evaluation dataset's vocabulary,
            // for the same reason the classification resource uses it.
            category?.ToLabel(),
            priority?.ToString(),
            isDraftEligible,
            RoutingGates.Describe(failedGates),
            DraftStates.From(draftCitationsValid),
            reviewDecision?.ToString(),
            isResolved);
}
