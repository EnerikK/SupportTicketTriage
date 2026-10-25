namespace SupportTicketTriage.Api.Contracts;

/// One page of the review queue.
///
/// An envelope rather than a bare array, because a page of rows is not
/// self-describing: nothing in a list of ten tickets says whether it is the
/// whole queue or a tenth of it, and "Showing 21-30 of 301" needs all four
/// values to render at all.
///
/// <c>TotalPages</c> is deliberately absent. It is
/// <c>ceil(TotalCount / PageSize)</c> and nothing else, so carrying it would
/// put one number in two places and invite them to disagree.
///
/// <see cref="Page"/> and <see cref="PageSize"/> report what was actually
/// applied, not what was asked for. A request is clamped rather than
/// rejected, so these are how a caller finds out it was.
public sealed record TicketQueuePageResponse(
    IReadOnlyList<TicketQueueItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
