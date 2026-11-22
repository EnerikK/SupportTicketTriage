using SupportTicketTriage.Application.Dashboard;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Dashboard;

namespace SupportTicketTriage.Api.Contracts;

public sealed record DashboardHeadlineResponse(
    int Received,
    int Routed,
    int RoutedToManualTriage,
    int DraftDecisions,
    int ApprovedAsWritten,
    double? MedianSecondsToResolution)
{
    public static DashboardHeadlineResponse FromResult(DashboardHeadline headline) =>
        new(
            headline.Received,
            headline.Routed,
            headline.RoutedToManualTriage,
            headline.DraftDecisions,
            headline.ApprovedAsWritten,
            headline.MedianSecondsToResolution);
}

public sealed record DashboardDayResponse(DateOnly Day, int Received, int ResolvedByReviewer);

public sealed record DashboardRoutingResponse(
    int BothPassed,
    int RetrievalSimilarityFailed,
    int ClassificationScoreFailed,
    int BothFailed,
    int NotTriaged);

public sealed record DashboardReviewsResponse(
    int ApprovedAsWritten,
    int EditedThenApproved,
    int Rejected,
    int ResolvedManually);

public sealed record DashboardDraftsResponse(
    int Generated,
    int Withheld,
    int ValidCitingNoSource,
    double? AverageCitedPerValidDraft);

public sealed record DashboardCategoryResponse(string Category, int Count);

/// `Reason` is one of DraftRejected, DraftWithheld, GateFailed or NotTriaged.
/// `FailedGates` names the gates, as the queue does, and is empty unless the
/// reason is GateFailed.
public sealed record DashboardAttentionResponse(
    Guid TicketId,
    string Subject,
    DateTimeOffset ReceivedAt,
    string? Category,
    string Reason,
    IReadOnlyList<string> FailedGates);

/// Pipeline health over one period, with the period before it for comparison
/// (ADR-046).
///
/// Counts, not percentages: shares and deltas are the client's to compute.
/// The median is in seconds, and null when nothing was resolved by a
/// reviewer in the period. The last day of `Volume` is today, so far.
///
/// The breakdowns cover the current period only. `Categories` always lists
/// all six; tickets with no classification are counted in `Unclassified`
/// rather than under a null category. `NeedsAttention` is current state, not
/// filtered by period: at most ten tickets, newest first.
public sealed record DashboardResponse(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset PreviousFrom,
    DashboardHeadlineResponse Current,
    DashboardHeadlineResponse Previous,
    IReadOnlyList<DashboardDayResponse> Volume,
    DashboardRoutingResponse Routing,
    DashboardReviewsResponse Reviews,
    DashboardDraftsResponse Drafts,
    IReadOnlyList<DashboardCategoryResponse> Categories,
    int Unclassified,
    IReadOnlyList<DashboardAttentionResponse> NeedsAttention)
{
    public static DashboardResponse Create(DashboardPeriod period, DashboardSnapshot snapshot) =>
        new(
            period.Range,
            period.From,
            period.To,
            period.PreviousFrom,
            DashboardHeadlineResponse.FromResult(snapshot.Current),
            DashboardHeadlineResponse.FromResult(snapshot.Previous),
            snapshot.Volume
                .Select(d => new DashboardDayResponse(d.Day, d.Received, d.ResolvedByReviewer))
                .ToArray(),
            new DashboardRoutingResponse(
                snapshot.Routing.BothPassed,
                snapshot.Routing.RetrievalSimilarityFailed,
                snapshot.Routing.ClassificationScoreFailed,
                snapshot.Routing.BothFailed,
                snapshot.Routing.NotTriaged),
            new DashboardReviewsResponse(
                snapshot.Reviews.ApprovedAsWritten,
                snapshot.Reviews.EditedThenApproved,
                snapshot.Reviews.Rejected,
                snapshot.Reviews.ResolvedManually),
            new DashboardDraftsResponse(
                snapshot.Drafts.Generated,
                snapshot.Drafts.Withheld,
                snapshot.Drafts.ValidCitingNoSource,
                snapshot.Drafts.AverageCitedPerValidDraft),
            snapshot.Categories
                .Select(c => new DashboardCategoryResponse(c.Category.ToLabel(), c.Count))
                .ToArray(),
            snapshot.Unclassified,
            snapshot.NeedsAttention
                .Select(a => new DashboardAttentionResponse(
                    a.TicketId,
                    a.Subject,
                    a.ReceivedAt,
                    a.Category?.ToLabel(),
                    a.Reason.ToString(),
                    RoutingGates.Describe(a.FailedGates)))
                .ToArray());
}
