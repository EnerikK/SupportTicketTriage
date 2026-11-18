using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Dashboard;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Dashboard;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// Exact counts need a database holding nothing but what the test seeded, so
/// each test gets a throwaway one. Tickets are created through the domain and
/// then moved in time with SQL. The domain stamps the current time and offers
/// no way to backdate, deliberately (ADR-046), so only tests do this.
[Collection(nameof(TicketApiCollection))]
public class DashboardServiceTests(TicketApiFactory factory)
{
    private const string DraftText = "Both charges relate to a single order.";

    // A fixed "now" makes the periods known: this week runs from 2026-06-09
    // up to 2026-06-16, and the week before it from 2026-06-02.
    private static readonly DashboardPeriod Week = Period("7d");

    private static DashboardPeriod Period(string range)
    {
        DashboardPeriod.TryCreate(range, new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), out var period);
        return period!;
    }

    private static async Task<DashboardSnapshot> SnapshotAsync(ThrowawayDatabase database, DashboardPeriod period)
    {
        await using var db = database.CreateContext();
        return await new DashboardService(db).GetAsync(period);
    }

    private static async Task<(SupportTicketTriageDbContext Db, Seed Seed)> MigrateAsync(ThrowawayDatabase database)
    {
        var db = database.CreateContext();
        await db.Database.MigrateAsync();
        return (db, new Seed(db));
    }

    [Fact]
    public async Task Received_CountsTicketsByCreation_InHalfOpenPeriods()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var microsecond = TimeSpan.FromTicks(10);
        await seed.TicketAsync(Week.From);
        await seed.TicketAsync(Week.To - microsecond);
        await seed.TicketAsync(Week.To);
        await seed.TicketAsync(Week.From - microsecond);
        await seed.TicketAsync(Week.PreviousFrom);
        await seed.TicketAsync(Week.PreviousFrom - microsecond);

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(2, snapshot.Current.Received);
        Assert.Equal(2, snapshot.Previous.Received);
    }

    /// A ticket triaged again is counted once, by the decision that stands,
    /// in the period that decision was made.
    [Fact]
    public async Task RoutedToManualTriage_CountsOnlyTheLatestDecision()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var retriaged = await seed.TicketAsync(Week.PreviousFrom.AddDays(1));
        await seed.RouteAsync(retriaged, Week.PreviousFrom.AddDays(1), RoutingGate.RetrievalSimilarity);
        await seed.RouteAsync(retriaged, Week.From.AddDays(1));

        var gated = await seed.TicketAsync(Week.From.AddDays(2));
        await seed.RouteAsync(gated, Week.From.AddDays(2), RoutingGate.RetrievalSimilarity);

        var earlier = await seed.TicketAsync(Week.PreviousFrom.AddDays(2));
        await seed.RouteAsync(earlier, Week.PreviousFrom.AddDays(2));

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(2, snapshot.Current.Routed);
        Assert.Equal(1, snapshot.Current.RoutedToManualTriage);
        Assert.Equal(1, snapshot.Previous.Routed);
        Assert.Equal(0, snapshot.Previous.RoutedToManualTriage);
    }

    /// A manual resolution judged no draft, so it counts as neither an
    /// approval nor a rejection.
    [Fact]
    public async Task ApprovedAsWritten_IsCountedAgainstApprovalsAndRejectionsOnly()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Approved, at);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Approved, at, edited: true);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Rejected, at);
        await seed.ResolveManuallyAsync(await seed.TicketAsync(at), at);

        var before = Week.PreviousFrom.AddDays(1);
        await seed.ReviewDraftAsync(await seed.TicketAsync(before), ReviewDecision.Approved, before);

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(3, snapshot.Current.DraftDecisions);
        Assert.Equal(1, snapshot.Current.ApprovedAsWritten);
        Assert.Equal(1, snapshot.Previous.DraftDecisions);
        Assert.Equal(1, snapshot.Previous.ApprovedAsWritten);
    }

    /// Runs the one raw SQL statement against PostgreSQL, which is the only
    /// check it gets: nothing compares it with the model at compile time.
    [Fact]
    public async Task MedianTimeToResolution_CoversReviewerResolutions_AndNotImportedOnes()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var created = Week.From.AddDays(1);
        await seed.ReviewDraftAsync(await seed.TicketAsync(created), ReviewDecision.Approved, created.AddHours(1));
        await seed.ReviewDraftAsync(await seed.TicketAsync(created), ReviewDecision.Approved, created.AddHours(3), edited: true);
        await seed.ResolveManuallyAsync(await seed.TicketAsync(created), created.AddHours(5));
        await seed.ImportAsync(await seed.TicketAsync(created), created.AddHours(100));

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(TimeSpan.FromHours(3).TotalSeconds, snapshot.Current.MedianSecondsToResolution!.Value, precision: 3);
        Assert.Null(snapshot.Previous.MedianSecondsToResolution);
    }

    /// Run with the session in UTC+05:45, so that a ticket created at 23:59
    /// UTC would land on the next day if the grouping used the session's time
    /// zone rather than UTC. A container running in UTC could not catch that.
    [Fact]
    public async Task Volume_HasAZeroFilledPointForEveryUtcDay()
    {
        await using var database = ThrowawayDatabase.Create(factory, sessionTimeZone: "Asia/Kathmandu");
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        // Otherwise an ignored setting would let this pass without testing anything.
        Assert.Equal(
            "Asia/Kathmandu",
            await db.Database.SqlQuery<string>($"""SELECT current_setting('TimeZone') AS "Value" """).SingleAsync());

        await seed.TicketAsync(Week.From.AddHours(1));
        var approved = await seed.TicketAsync(Week.From.AddHours(2));
        await seed.ReviewDraftAsync(approved, ReviewDecision.Approved, Week.From.AddDays(3).AddHours(1));

        var imported = await seed.TicketAsync(Week.PreviousFrom);
        await seed.ImportAsync(imported, Week.From.AddDays(3).AddHours(2));

        await seed.TicketAsync(Week.From.AddDays(3).AddHours(23).AddMinutes(59));

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(Week.EachDay(), snapshot.Volume.Select(d => d.Day));
        Assert.Equal(new[] { 2, 0, 0, 1, 0, 0, 0 }, snapshot.Volume.Select(d => d.Received));
        Assert.Equal(new[] { 0, 0, 0, 1, 0, 0, 0 }, snapshot.Volume.Select(d => d.ResolvedByReviewer));
    }

    /// The property ADR-012 protects for the queue, tested by behaviour: the
    /// same number of statements whether there are a few tickets or many.
    [Fact]
    public async Task TheNumberOfStatements_DoesNotGrowWithTheNumberOfTickets()
    {
        var sql = new SqlCapturingLoggerProvider();
        await using var database = ThrowawayDatabase.Create(factory, sql: sql);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        async Task AddTicketsAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var at = Week.From.AddHours(i + 1);
                var ticket = await seed.TicketAsync(at);
                await seed.RouteAsync(ticket, at);
                await seed.ReviewDraftAsync(ticket, ReviewDecision.Approved, at.AddMinutes(30));
            }
        }

        await AddTicketsAsync(2);
        sql.Clear();
        await SnapshotAsync(database, Week);
        var few = sql.Commands.Count;

        await AddTicketsAsync(20);
        sql.Clear();
        await SnapshotAsync(database, Week);

        Assert.True(few > 0);
        Assert.Equal(few, sql.Commands.Count);
    }

    /// Each ticket counted once, by the decision that stands, under the gates
    /// that failed; a ticket never routed counts as untriaged only when it
    /// arrived in the period, since it has no decision time to go by.
    [Fact]
    public async Task Routing_IsSplitByTheGatesThatFailed_PlusTicketsNeverTriaged()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        await seed.RouteAsync(await seed.TicketAsync(at), at);
        await seed.RouteAsync(await seed.TicketAsync(at), at, RoutingGate.RetrievalSimilarity);
        await seed.RouteAsync(await seed.TicketAsync(at), at, RoutingGate.ClassificationScore);
        await seed.RouteAsync(
            await seed.TicketAsync(at), at, RoutingGate.ClassificationScore | RoutingGate.RetrievalSimilarity);

        var retriaged = await seed.TicketAsync(at);
        await seed.RouteAsync(retriaged, at, RoutingGate.RetrievalSimilarity);
        await seed.RouteAsync(retriaged, at.AddHours(1));

        await seed.TicketAsync(at);
        await seed.TicketAsync(Week.PreviousFrom.AddDays(1));

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(
            new DashboardRouting(
                BothPassed: 2,
                RetrievalSimilarityFailed: 1,
                ClassificationScoreFailed: 1,
                BothFailed: 1,
                NotTriaged: 1),
            snapshot.Routing);
    }

    [Fact]
    public async Task ReviewDecisions_AreCountedForAllFour()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Approved, at);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Approved, at, edited: true);
        await seed.ReviewDraftAsync(await seed.TicketAsync(at), ReviewDecision.Rejected, at);
        await seed.ResolveManuallyAsync(await seed.TicketAsync(at), at);
        await seed.ResolveManuallyAsync(await seed.TicketAsync(at), at);

        var before = Week.PreviousFrom.AddDays(1);
        await seed.ResolveManuallyAsync(await seed.TicketAsync(before), before);

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(
            new DashboardReviews(ApprovedAsWritten: 1, EditedThenApproved: 1, Rejected: 1, ResolvedManually: 2),
            snapshot.Reviews);
    }

    /// The failures sit beside the rest: a withheld draft, and a valid draft
    /// that cited nothing, which counts as zero in the average.
    [Fact]
    public async Task Drafts_CountWithheldAndUnsupportedDrafts_BesideTheRest()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        await seed.DraftAsync(await seed.TicketAsync(at), at);
        await seed.DraftAsync(await seed.TicketAsync(at), at, citesSource: false);
        await seed.DraftAsync(await seed.TicketAsync(at), at, withheld: true);
        await seed.DraftAsync(await seed.TicketAsync(at), Week.PreviousFrom.AddDays(1));

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(
            new DashboardDrafts(Generated: 3, Withheld: 1, ValidCitingNoSource: 1, AverageCitedPerValidDraft: 0.5),
            snapshot.Drafts);
    }

    [Fact]
    public async Task Drafts_HaveNoAverage_WhenNoDraftPassedValidation()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        await seed.DraftAsync(await seed.TicketAsync(at), at, withheld: true);

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(1, snapshot.Drafts.Withheld);
        Assert.Null(snapshot.Drafts.AverageCitedPerValidDraft);
    }

    /// A reclassified ticket counts once, under its latest category. The
    /// ticket's arrival decides the period, not the classification's.
    [Fact]
    public async Task Categories_UseEachTicketsLatestClassification_AndListAllSix()
    {
        await using var database = ThrowawayDatabase.Create(factory);
        var (db, seed) = await MigrateAsync(database);
        await using var _ = db;

        var at = Week.From.AddDays(1);
        var reclassified = await seed.TicketAsync(at);
        await seed.ClassifyAsync(reclassified, TicketCategory.Billing, at);
        await seed.ClassifyAsync(reclassified, TicketCategory.TechnicalFault, at.AddHours(1));

        await seed.ClassifyAsync(await seed.TicketAsync(at), TicketCategory.Billing, at);
        await seed.TicketAsync(at);

        var before = Week.PreviousFrom.AddDays(1);
        await seed.ClassifyAsync(await seed.TicketAsync(before), TicketCategory.Billing, before);

        var snapshot = await SnapshotAsync(database, Week);

        Assert.Equal(Enum.GetValues<TicketCategory>(), snapshot.Categories.Select(c => c.Category));
        Assert.Equal(1, snapshot.Categories.Single(c => c.Category == TicketCategory.Billing).Count);
        Assert.Equal(1, snapshot.Categories.Single(c => c.Category == TicketCategory.TechnicalFault).Count);
        Assert.Equal(2, snapshot.Categories.Sum(c => c.Count));
        Assert.Equal(1, snapshot.Unclassified);
    }

    [Fact]
    public async Task GetDashboard_ServesThirtyDaysByDefault_AndRefusesAnUnknownRange()
    {
        var client = factory.CreateClient();

        var dashboard = await client.GetFromJsonAsync<DashboardResponse>("/dashboard");

        Assert.Equal("30d", dashboard!.Range);
        Assert.Equal(dashboard.To.AddDays(-30), dashboard.From);
        Assert.Equal(30, dashboard.Volume.Count);
        Assert.Equal(Enum.GetNames<TicketCategory>(), dashboard.Categories.Select(c => c.Category));

        var refused = await client.GetAsync("/dashboard?range=1d");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// Seeds through the domain, then moves each row to the time a test needs.
    private sealed class Seed(SupportTicketTriageDbContext db)
    {
        private Ticket? _source;

        public async Task<Ticket> TicketAsync(DateTimeOffset createdAt)
        {
            var ticket = Ticket.Create("A subject", "A body.");
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "Tickets" SET "CreatedAt" = {createdAt} WHERE "Id" = {ticket.Id}""");
            return ticket;
        }

        /// A routing decision that fails exactly the gates asked for, by
        /// putting each input on the failing or passing side of its floor.
        public async Task RouteAsync(Ticket ticket, DateTimeOffset at, RoutingGate failed = RoutingGate.None)
        {
            var score = failed.HasFlag(RoutingGate.ClassificationScore) ? 0.10 : 0.95;
            var similarity = failed.HasFlag(RoutingGate.RetrievalSimilarity) ? 0.10 : 0.98;

            var decision = TicketRoutingDecision.Create(
                ticket.Id,
                RoutingDecision.Evaluate(score, similarity, RoutingThresholds.Default));
            db.TicketRoutingDecisions.Add(decision);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "TicketRoutingDecisions" SET "CreatedAt" = {at} WHERE "Id" = {decision.Id}""");
        }

        public async Task ClassifyAsync(Ticket ticket, TicketCategory category, DateTimeOffset at)
        {
            var classification = TicketClassification.Create(
                ticket.Id, category, TicketPriority.Normal, 0.9, "redacted", "v1", "test-chat-model", "v1");
            db.TicketClassifications.Add(classification);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "TicketClassifications" SET "CreatedAt" = {at} WHERE "Id" = {classification.Id}""");
        }

        /// A draft grounded in the shared source. By default it cites that
        /// source; it can instead cite nothing, or cite a ticket it was never
        /// shown, which makes it a withheld draft.
        public async Task<TicketDraft> DraftAsync(
            Ticket ticket,
            DateTimeOffset at,
            bool citesSource = true,
            bool withheld = false)
        {
            var source = await SourceAsync();
            var retrieved = new[] { new RetrievedSource(source.Id, 1, 0.9) };
            Guid[] cited = withheld ? [Guid.CreateVersion7()] : citesSource ? [source.Id] : [];

            var draft = TicketDraft.Create(
                ticket.Id,
                DraftText,
                retrieved,
                CitationValidation.Validate(cited, retrieved.Select(s => s.TicketId).ToHashSet()),
                "test-chat-model",
                "v1",
                "v1");
            db.TicketDrafts.Add(draft);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "TicketDrafts" SET "CreatedAt" = {at} WHERE "Id" = {draft.Id}""");
            return draft;
        }

        /// A review of a citation-valid draft made at the same time. An
        /// approval also resolves the ticket, at that time.
        public async Task ReviewDraftAsync(Ticket ticket, ReviewDecision decision, DateTimeOffset at, bool edited = false)
        {
            var draft = await DraftAsync(ticket, at);

            ReviewSubmission.TryCreate(
                decision,
                DraftText,
                edited ? "An edited reply." : null,
                decision == ReviewDecision.Rejected ? "Cites the wrong precedent." : null,
                out var submission,
                out _);

            await RecordAsync(ticket, draft.Id, submission!, ResolutionSource.ApprovedDraft, at);
        }

        public async Task ResolveManuallyAsync(Ticket ticket, DateTimeOffset at)
        {
            ReviewSubmission.TryCreateManualResolution("Written by hand.", null, out var submission, out _);
            await RecordAsync(ticket, null, submission!, ResolutionSource.ManualReply, at);
        }

        public async Task ImportAsync(Ticket ticket, DateTimeOffset resolvedAt)
        {
            ticket.Resolve("Imported into the corpus.", ResolutionSource.Imported);
            await db.SaveChangesAsync();
            await SetResolvedAtAsync(ticket.Id, resolvedAt);
        }

        private async Task RecordAsync(
            Ticket ticket,
            Guid? draftId,
            ReviewSubmission submission,
            ResolutionSource source,
            DateTimeOffset at)
        {
            var review = TicketReview.Create(ticket.Id, draftId, submission);
            db.TicketReviews.Add(review);

            var resolves = submission.FinalText is not null;
            if (resolves)
            {
                ticket.Resolve(submission.FinalText!, source);
            }

            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "TicketReviews" SET "CreatedAt" = {at} WHERE "Id" = {review.Id}""");

            if (resolves)
            {
                await SetResolvedAtAsync(ticket.Id, at);
            }
        }

        private async Task SetResolvedAtAsync(Guid ticketId, DateTimeOffset at) =>
            await db.Database.ExecuteSqlAsync($"""UPDATE "Tickets" SET "ResolvedAt" = {at} WHERE "Id" = {ticketId}""");

        /// The ticket every seeded draft cites, created and resolved well
        /// outside any period under test.
        private async Task<Ticket> SourceAsync()
        {
            if (_source is null)
            {
                var longAgo = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                _source = await TicketAsync(longAgo);
                await ImportAsync(_source, longAgo);
            }

            return _source;
        }
    }
}
