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
        await seed.RouteAsync(retriaged, eligible: false, Week.PreviousFrom.AddDays(1));
        await seed.RouteAsync(retriaged, eligible: true, Week.From.AddDays(1));

        var gated = await seed.TicketAsync(Week.From.AddDays(2));
        await seed.RouteAsync(gated, eligible: false, Week.From.AddDays(2));

        var earlier = await seed.TicketAsync(Week.PreviousFrom.AddDays(2));
        await seed.RouteAsync(earlier, eligible: true, Week.PreviousFrom.AddDays(2));

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
                await seed.RouteAsync(ticket, eligible: true, at);
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

    [Fact]
    public async Task GetDashboard_ServesThirtyDaysByDefault_AndRefusesAnUnknownRange()
    {
        var client = factory.CreateClient();

        var dashboard = await client.GetFromJsonAsync<DashboardResponse>("/dashboard");

        Assert.Equal("30d", dashboard!.Range);
        Assert.Equal(dashboard.To.AddDays(-30), dashboard.From);
        Assert.Equal(30, dashboard.Volume.Count);

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

        public async Task RouteAsync(Ticket ticket, bool eligible, DateTimeOffset at)
        {
            var decision = TicketRoutingDecision.Create(
                ticket.Id,
                RoutingDecision.Evaluate(0.95, eligible ? 0.98 : 0.10, RoutingThresholds.Default));
            db.TicketRoutingDecisions.Add(decision);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"""UPDATE "TicketRoutingDecisions" SET "CreatedAt" = {at} WHERE "Id" = {decision.Id}""");
        }

        /// A review of a citation-valid draft. An approval also resolves the
        /// ticket, at the same time.
        public async Task ReviewDraftAsync(Ticket ticket, ReviewDecision decision, DateTimeOffset at, bool edited = false)
        {
            var source = await SourceAsync();
            var retrieved = new[] { new RetrievedSource(source.Id, 1, 0.9) };
            var draft = TicketDraft.Create(
                ticket.Id,
                DraftText,
                retrieved,
                CitationValidation.Validate([source.Id], retrieved.Select(s => s.TicketId).ToHashSet()),
                "test-chat-model",
                "v1",
                "v1");
            db.TicketDrafts.Add(draft);

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
