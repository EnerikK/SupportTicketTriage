using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Application.Dashboard;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Application.Routing;
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

/// Tickets by the routing decision that stands, split by which gates failed,
/// plus tickets received in the period that were never routed at all.
public sealed record DashboardRouting(
    int BothPassed,
    int RetrievalSimilarityFailed,
    int ClassificationScoreFailed,
    int BothFailed,
    int NotTriaged);

public sealed record DashboardReviews(
    int ApprovedAsWritten,
    int EditedThenApproved,
    int Rejected,
    int ResolvedManually);

/// The average is null when no draft passed validation, rather than zero,
/// which would read as "they cited nothing".
public sealed record DashboardDrafts(
    int Generated,
    int Withheld,
    int ValidCitingNoSource,
    double? AverageCitedPerValidDraft);

public sealed record DashboardCategory(TicketCategory Category, int Count);

/// Why an unresolved ticket has no draft a reviewer can approve. Numbered
/// from 1, like the other closed sets here, so an unset value is never a
/// valid reason.
public enum AttentionReason
{
    /// The reviewer rejected the draft. A rejected ticket cannot yet be
    /// resolved, because the rejection is its one review (ADR-011).
    DraftRejected = 1,

    /// The latest draft cited evidence it was never shown, and nobody has
    /// written past it yet.
    DraftWithheld = 2,

    /// The routing decision that stands failed at least one gate.
    GateFailed = 3,

    /// No routing decision exists at all.
    NotTriaged = 4,
}

public sealed record DashboardAttentionItem(
    Guid TicketId,
    string Subject,
    DateTimeOffset ReceivedAt,
    TicketCategory? Category,
    AttentionReason Reason,
    RoutingGate FailedGates);

public sealed record DashboardSnapshot(
    DashboardHeadline Current,
    DashboardHeadline Previous,
    IReadOnlyList<DashboardDay> Volume,
    DashboardRouting Routing,
    DashboardReviews Reviews,
    DashboardDrafts Drafts,
    IReadOnlyList<DashboardCategory> Categories,
    int Unclassified,
    IReadOnlyList<DashboardAttentionItem> NeedsAttention);

/// Read-only aggregates for the pipeline dashboard (ADR-046).
///
/// Each figure is one aggregate statement, so the number of statements is
/// fixed and never grows with the number of tickets. The headline figures
/// cover the current and the previous period in the same statement; the
/// breakdowns cover the current period only. Each is counted by its own
/// event's timestamp: received by the ticket's creation, routing by the
/// decision, reviews by the review, drafts by the draft, resolution by the
/// resolution.
public sealed class DashboardService(SupportTicketTriageDbContext db)
{
    public const int NeedsAttentionLimit = 10;

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
        // FailedGates is a flags enum stored as its name, so each combination
        // is a plain string comparison in SQL.
        const RoutingGate bothGates = RoutingGate.ClassificationScore | RoutingGate.RetrievalSimilarity;
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
                BothPassed = g.Count(r => r.CreatedAt >= from && r.FailedGates == RoutingGate.None),
                RetrievalSimilarityFailed = g.Count(r => r.CreatedAt >= from && r.FailedGates == RoutingGate.RetrievalSimilarity),
                ClassificationScoreFailed = g.Count(r => r.CreatedAt >= from && r.FailedGates == RoutingGate.ClassificationScore),
                BothFailed = g.Count(r => r.CreatedAt >= from && r.FailedGates == bothGates),
            })
            .FirstOrDefaultAsync(ct);

        // All four decisions for the breakdown. The headline's "approved as
        // written" is over approvals and rejections only: a manual resolution
        // judged no draft, so it is neither.
        var reviews = await db.TicketReviews
            .Where(r => r.CreatedAt >= previousFrom && r.CreatedAt < to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                AsWritten = g.Count(r => r.CreatedAt >= from && r.Decision == ReviewDecision.Approved && !r.WasEdited),
                Edited = g.Count(r => r.CreatedAt >= from && r.Decision == ReviewDecision.Approved && r.WasEdited),
                Rejected = g.Count(r => r.CreatedAt >= from && r.Decision == ReviewDecision.Rejected),
                Manual = g.Count(r => r.CreatedAt >= from && r.Decision == ReviewDecision.ResolvedManually),
                PreviousAsWritten = g.Count(r => r.CreatedAt < from && r.Decision == ReviewDecision.Approved && !r.WasEdited),
                PreviousDraftDecisions = g.Count(r => r.CreatedAt < from
                    && (r.Decision == ReviewDecision.Approved || r.Decision == ReviewDecision.Rejected)),
            })
            .FirstOrDefaultAsync(ct);

        // Tickets received in the period that no stage has touched. A ticket
        // with no routing decision has no decision timestamp to count by, so
        // it is counted by when it arrived.
        var untouched = await db.Tickets
            .Where(t => t.CreatedAt >= from && t.CreatedAt < to)
            .Select(t => new
            {
                Routed = db.TicketRoutingDecisions.Any(r => r.TicketId == t.Id),
                Classified = db.TicketClassifications.Any(c => c.TicketId == t.Id),
            })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                NotTriaged = g.Count(x => !x.Routed),
                Unclassified = g.Count(x => !x.Classified),
            })
            .FirstOrDefaultAsync(ct);

        // Withheld drafts and valid drafts citing nothing are counted beside
        // the rest, because hiding them is what ADR-046 rules out.
        var drafts = await db.TicketDrafts
            .Where(d => d.CreatedAt >= from && d.CreatedAt < to)
            .Select(d => new { d.CitationsValid, Cited = d.Sources.Count(s => s.WasCited) })
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Generated = g.Count(),
                Withheld = g.Count(x => !x.CitationsValid),
                ValidCitingNone = g.Count(x => x.CitationsValid && x.Cited == 0),
                AverageCited = g.Where(x => x.CitationsValid).Average(x => (double?)x.Cited),
            })
            .FirstOrDefaultAsync(ct);

        // Each ticket's latest classification, the same "latest wins" rule as
        // routing; categories are a property of the ticket, so the ticket's
        // arrival decides the period.
        var byCategory = await db.TicketClassifications
            .Where(c => !db.TicketClassifications.Any(n => n.TicketId == c.TicketId && n.CreatedAt > c.CreatedAt))
            .Join(
                db.Tickets.Where(t => t.CreatedAt >= from && t.CreatedAt < to),
                c => c.TicketId,
                t => t.Id,
                (c, _) => c.Category)
            .GroupBy(category => category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Category, x => x.Count, ct);

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

        // Current state, not filtered by period: unresolved tickets with no
        // draft a reviewer can approve. Left out on purpose are an eligible
        // ticket with no draft yet, which can still be drafted, and a valid
        // draft awaiting review, which can be approved. Id breaks ties so that
        // the ten rows are always the same ten.
        //
        // Each signal is the latest of its kind. Because the filter refers to
        // them, EF writes these as correlated LIMIT 1 subqueries rather than
        // the window-function joins the queue gets, and the WHERE repeats some
        // of them. Each is an index lookup on (TicketId, CreatedAt DESC), over
        // unresolved tickets only: still one statement, but its work grows
        // with the unresolved backlog. Fine in the thousands; past that, the
        // denormalised state columns ADR-012 already names are the fix.
        var attentionRows = await db.Tickets
            .Where(t => t.Resolution == null)
            .Select(t => new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                Category = db.TicketClassifications
                    .Where(c => c.TicketId == t.Id)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => (TicketCategory?)c.Category)
                    .FirstOrDefault(),
                RoutingEligible = db.TicketRoutingDecisions
                    .Where(r => r.TicketId == t.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => (bool?)r.IsDraftEligible)
                    .FirstOrDefault(),
                FailedGates = db.TicketRoutingDecisions
                    .Where(r => r.TicketId == t.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => (RoutingGate?)r.FailedGates)
                    .FirstOrDefault(),
                DraftValid = db.TicketDrafts
                    .Where(d => d.TicketId == t.Id)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (bool?)d.CitationsValid)
                    .FirstOrDefault(),
                Decision = db.TicketReviews
                    .Where(v => v.TicketId == t.Id)
                    .Select(v => (ReviewDecision?)v.Decision)
                    .FirstOrDefault(),
            })
            .Where(x => x.Decision == ReviewDecision.Rejected
                || (x.Decision == null && x.DraftValid == false)
                || (x.Decision == null && x.DraftValid == null && x.RoutingEligible == false)
                || x.RoutingEligible == null)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(NeedsAttentionLimit)
            .ToListAsync(ct);

        // The order of precedence when more than one applies: a recorded
        // rejection outranks the draft and the routing behind it.
        var needsAttention = attentionRows
            .Select(x =>
            {
                var reason = x.Decision == ReviewDecision.Rejected ? AttentionReason.DraftRejected
                    : x.DraftValid == false ? AttentionReason.DraftWithheld
                    : x.RoutingEligible == false ? AttentionReason.GateFailed
                    : AttentionReason.NotTriaged;

                return new DashboardAttentionItem(
                    x.Id,
                    x.Subject,
                    x.CreatedAt,
                    x.Category,
                    reason,
                    reason == AttentionReason.GateFailed ? x.FailedGates ?? RoutingGate.None : RoutingGate.None);
            })
            .ToArray();

        // Every category, with zeros, so the client never has to know the
        // closed set to draw an empty bar.
        var categories = Enum.GetValues<TicketCategory>()
            .Select(category => new DashboardCategory(category, byCategory.GetValueOrDefault(category)))
            .ToArray();

        return new DashboardSnapshot(
            new DashboardHeadline(
                received?.Current ?? 0,
                routing?.Current ?? 0,
                routing?.CurrentManual ?? 0,
                (reviews?.AsWritten ?? 0) + (reviews?.Edited ?? 0) + (reviews?.Rejected ?? 0),
                reviews?.AsWritten ?? 0,
                median.Current),
            new DashboardHeadline(
                received?.Previous ?? 0,
                routing?.Previous ?? 0,
                routing?.PreviousManual ?? 0,
                reviews?.PreviousDraftDecisions ?? 0,
                reviews?.PreviousAsWritten ?? 0,
                median.Previous),
            volume,
            new DashboardRouting(
                routing?.BothPassed ?? 0,
                routing?.RetrievalSimilarityFailed ?? 0,
                routing?.ClassificationScoreFailed ?? 0,
                routing?.BothFailed ?? 0,
                untouched?.NotTriaged ?? 0),
            new DashboardReviews(
                reviews?.AsWritten ?? 0,
                reviews?.Edited ?? 0,
                reviews?.Rejected ?? 0,
                reviews?.Manual ?? 0),
            new DashboardDrafts(
                drafts?.Generated ?? 0,
                drafts?.Withheld ?? 0,
                drafts?.ValidCitingNone ?? 0,
                drafts?.AverageCited),
            categories,
            untouched?.Unclassified ?? 0,
            needsAttention);
    }

    private sealed class MedianRow
    {
        public double? Current { get; init; }
        public double? Previous { get; init; }
    }
}
