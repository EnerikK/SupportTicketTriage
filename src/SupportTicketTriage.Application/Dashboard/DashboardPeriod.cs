namespace SupportTicketTriage.Application.Dashboard;

/// The window a dashboard request reports on, and the window before it.
///
/// Whole UTC days, ending at the start of tomorrow so that today is included
/// as far as it has got (ADR-046, as amended). Half-open throughout: an event
/// at exactly <see cref="From"/> belongs to this period, and one at exactly
/// <see cref="To"/> belongs to the next.
public sealed record DashboardPeriod(
    string Range,
    int Days,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset PreviousFrom)
{
    public static bool TryCreate(string? range, DateTimeOffset now, out DashboardPeriod? period)
    {
        period = null;

        // A closed set of words, matched rather than parsed, so "30" or "1d"
        // is a 400 naming what was expected instead of a guess.
        var days = range?.Trim().ToLowerInvariant() switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            _ => 0,
        };

        if (days == 0)
        {
            return false;
        }

        var to = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var from = to.AddDays(-days);

        period = new DashboardPeriod($"{days}d", days, from, to, from.AddDays(-days));
        return true;
    }

    /// Every UTC day in the period, oldest first, so that a series carries a
    /// zero for a day with no events instead of skipping it.
    public IReadOnlyList<DateOnly> EachDay()
    {
        var first = DateOnly.FromDateTime(From.UtcDateTime);
        return Enumerable.Range(0, Days).Select(first.AddDays).ToArray();
    }
}
