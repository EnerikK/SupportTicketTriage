using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Application.Dashboard;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Infrastructure.Dashboard;

/// The four headline figures for one period. Counts, never percentages: the
/// client works out shares, so every rounding decision is made in one place.
public sealed record DashboardHeadline(
    int Received,
    int Routed,
    int RoutedToManualTriage,
    int DraftDecisions,
    int ApprovedAsWritten,
    double? MedianSecondsToResolution);

public sealed record DashboardDay(DateOnly Day, int Received, int ResolvedByReviewer);

public sealed record DashboardSnapshot(
    DashboardHeadline Current,
    DashboardHeadline Previous,
    IReadOnlyList<DashboardDay> Volume);

/// Read-only aggregates for the pipeline dashboard (ADR-046).
///
/// Each figure is one aggregate statement covering both the current and the
/// previous period, so the number of statements is fixed and never grows
/// with the number of tickets. Each is counted by its own event's timestamp:
/// received by the ticket's creation, routing by the decision, reviews by the
/// review, resolution by the resolution.
public sealed class DashboardService(SupportTicketTriageDbContext db)
{
    public async Task<DashboardSnapshot> GetAsync(DashboardPeriod period, CancellationToken ct = default)
    {
        var (from, to, previousFrom) = (period.From, period.To, period.PreviousFrom);

        var received = await db.Tickets
            .Where(t => t.CreatedAt >= previousFrom && t.CreatedAt < to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Current = g.Count(t => t.CreatedAt >= from),
                Previous = g.Count(t => t.CreatedAt < from),
            })
            .FirstOrDefaultAsync(ct);

        // The decision that stands: a re-triaged ticket is counted once, by
        // its latest decision, in the period that decision was made.
        var routing = await db.TicketRoutingDecisions
            .Where(r => r.CreatedAt >= previousFrom && r.CreatedAt < to)
            .Where(r => !db.TicketRoutingDecisions.Any(n => n.TicketId == r.TicketId && n.CreatedAt > r.CreatedAt))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Current = g.Count(r => r.CreatedAt >= from),
                CurrentManual = g.Count(r => r.CreatedAt >= from && !r.IsDraftEligible),
                Previous = g.Count(r => r.CreatedAt < from),
                PreviousManual = g.Count(r => r.CreatedAt < from && !r.IsDraftEligible),
            })
            .FirstOrDefaultAsync(ct);

        // Only decisions about a draft. A manual resolution judged no draft,
        // so it is neither an approval nor a rejection of one.
        var reviews = await db.TicketReviews
            .Where(r => r.CreatedAt >= previousFrom && r.CreatedAt < to)
            .Where(r => r.Decision == ReviewDecision.Approved || r.Decision == ReviewDecision.Rejected)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Current = g.Count(r => r.CreatedAt >= from),
                CurrentAsWritten = g.Count(r => r.CreatedAt >= from && r.Decision == ReviewDecision.Approved && !r.WasEdited),
                Previous = g.Count(r => r.CreatedAt < from),
                PreviousAsWritten = g.Count(r => r.CreatedAt < from && r.Decision == ReviewDecision.Approved && !r.WasEdited),
            })
            .FirstOrDefaultAsync(ct);

        // Raw SQL because Npgsql's EF provider has no translation for
        // percentile_cont (ADR-046). Not checked against the model at compile
        // time, so an integration test runs it. Imported corpus tickets were
        // resolved by a seed script, not a reviewer, and are excluded.
        var median = (await db.Database.SqlQuery<MedianRow>($"""
            SELECT
                percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM ("ResolvedAt" - "CreatedAt"))::double precision)
                    FILTER (WHERE "ResolvedAt" >= {from}) AS "Current",
                percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM ("ResolvedAt" - "CreatedAt"))::double precision)
                    FILTER (WHERE "ResolvedAt" < {from}) AS "Previous"
            FROM "Tickets"
            WHERE "ResolvedAt" >= {previousFrom}
              AND "ResolvedAt" < {to}
              AND "ResolutionSource" <> {nameof(ResolutionSource.Imported)}
            """).ToListAsync(ct)).Single();

        // Grouped by the UTC calendar day explicitly. Casting a timestamptz
        // straight to a date would use the session's time zone instead.
        var receivedByDay = await db.Tickets
            .Where(t => t.CreatedAt >= from && t.CreatedAt < to)
            .GroupBy(t => DateOnly.FromDateTime(t.CreatedAt.UtcDateTime))
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count, ct);

        var resolvedByDay = await db.Tickets
            .Where(t => t.ResolvedAt >= from && t.ResolvedAt < to)
            .Where(t => t.ResolutionSource != ResolutionSource.Imported)
            .GroupBy(t => DateOnly.FromDateTime(t.ResolvedAt!.Value.UtcDateTime))
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Day, x => x.Count, ct);

        var volume = period.EachDay()
            .Select(day => new DashboardDay(
                day,
                receivedByDay.GetValueOrDefault(day),
                resolvedByDay.GetValueOrDefault(day)))
            .ToArray();

        return new DashboardSnapshot(
            new DashboardHeadline(
                received?.Current ?? 0,
                routing?.Current ?? 0,
                routing?.CurrentManual ?? 0,
                reviews?.Current ?? 0,
                reviews?.CurrentAsWritten ?? 0,
                median.Current),
            new DashboardHeadline(
                received?.Previous ?? 0,
                routing?.Previous ?? 0,
                routing?.PreviousManual ?? 0,
                reviews?.Previous ?? 0,
                reviews?.PreviousAsWritten ?? 0,
                median.Previous),
            volume);
    }

    private sealed class MedianRow
    {
        public double? Current { get; init; }
        public double? Previous { get; init; }
    }
}
