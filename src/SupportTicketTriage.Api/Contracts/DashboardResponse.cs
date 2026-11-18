using SupportTicketTriage.Application.Dashboard;
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

/// Pipeline health over one period, with the period before it for comparison
/// (ADR-046).
///
/// Counts, not percentages: shares and deltas are the client's to compute.
/// The median is in seconds, and null when nothing was resolved by a
/// reviewer in the period. The last day of `Volume` is today, so far.
public sealed record DashboardResponse(
    string Range,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset PreviousFrom,
    DashboardHeadlineResponse Current,
    DashboardHeadlineResponse Previous,
    IReadOnlyList<DashboardDayResponse> Volume)
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
                .ToArray());
}
