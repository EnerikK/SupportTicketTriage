using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// The queue composes four append-only signals per ticket. These tests seed
/// those rows directly: what is under test is the projection, not the services
/// that produce them.
[Collection(nameof(TicketApiCollection))]
public class TicketQueueTests(TicketApiFactory factory)
{
    private const string DraftText = "Both charges relate to a single order.";
    private const int MaxPageSize = 100;

    private readonly HttpClient _client = factory.CreateClient();

    private static Ticket AddTicket(SupportTicketTriageDbContext db, string subject)
    {
        var ticket = Ticket.Create(subject, $"Body of {subject}.");
        db.Tickets.Add(ticket);
        return ticket;
    }

    private static void AddClassification(
        SupportTicketTriageDbContext db,
        Guid ticketId,
        TicketCategory category = TicketCategory.Billing,
        TicketPriority priority = TicketPriority.High) =>
        db.TicketClassifications.Add(TicketClassification.Create(
            ticketId, category, priority, 0.9, "redacted", "v1", "test-chat-model", "v1"));

    private static void AddRouting(
        SupportTicketTriageDbContext db,
        Guid ticketId,
        double? score,
        double? similarity) =>
        db.TicketRoutingDecisions.Add(TicketRoutingDecision.Create(
            ticketId, RoutingDecision.Evaluate(score, similarity, RoutingThresholds.Default)));

    private static TicketDraft AddDraft(
        SupportTicketTriageDbContext db,
        Guid ticketId,
        Guid sourceId,
        bool citationsValid)
    {
        var retrievalSet = new[] { new RetrievedSource(sourceId, 1, 0.9) };
        var cited = citationsValid ? [sourceId] : new[] { Guid.CreateVersion7() };

        var draft = TicketDraft.Create(
            ticketId,
            DraftText,
            retrievalSet,
            CitationValidation.Validate(cited, retrievalSet.Select(s => s.TicketId).ToHashSet()),
            "test-chat-model",
            "v1",
            "v1");

        db.TicketDrafts.Add(draft);
        return draft;
    }

    private async Task<TicketQueuePageResponse> QueuePageAsync(int page = 1, int pageSize = MaxPageSize) =>
        (await _client.GetFromJsonAsync<TicketQueuePageResponse>(
            $"/tickets?page={page}&pageSize={pageSize}"))!;

    /// Six test classes share one database and the queue is now paged, so a
    /// ticket seeded here is not reliably on any particular page. Walking
    /// keeps every assertion below about the projection rather than about how
    /// many tickets happened to exist by the time it ran.
    private async Task<TicketQueueItemResponse> QueueItemAsync(Guid ticketId)
    {
        for (var page = 1; ; page++)
        {
            var response = await QueuePageAsync(page);
            var match = response.Items.SingleOrDefault(item => item.Id == ticketId);

            if (match is not null)
            {
                return match;
            }

            Assert.True(
                (long)page * response.PageSize < response.TotalCount,
                $"Ticket {ticketId} never appeared in the queue.");
        }
    }

    [Fact]
    public async Task AFullyTriagedTicket_CarriesEverySignalInOneRow()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            var source = AddTicket(db, "queue-source");
            source.Resolve("A resolution.");

            var ticket = AddTicket(db, "queue-triaged");
            ticketId = ticket.Id;

            AddClassification(db, ticketId, TicketCategory.AccountAndLogin, TicketPriority.Low);
            AddRouting(db, ticketId, 0.95, 0.9);
            await db.SaveChangesAsync();

            var draft = AddDraft(db, ticketId, source.Id, citationsValid: true);
            await db.SaveChangesAsync();

            ReviewSubmission.TryCreate(ReviewDecision.Approved, DraftText, null, null, out var submission, out _);
            db.TicketReviews.Add(TicketReview.Create(ticketId, draft.Id, submission!));
            await db.SaveChangesAsync();
        }

        var item = await QueueItemAsync(ticketId);

        // The wire label, not the enum member name.
        Assert.Equal("Account & Login", item.Category);
        Assert.Equal("Low", item.Priority);
        Assert.True(item.IsDraftEligible);
        Assert.Empty(item.FailedGates);
        Assert.Equal(DraftStates.Ready, item.DraftState);
        Assert.Equal(nameof(ReviewDecision.Approved), item.ReviewDecision);
    }

    [Fact]
    public async Task ATicketWithNoTriageYet_ReportsAbsenceRatherThanDefaults()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            ticketId = AddTicket(db, "queue-untriaged").Id;
            await db.SaveChangesAsync();
        }

        var item = await QueueItemAsync(ticketId);

        // Nulls rather than a first enum member standing in for "unknown".
        Assert.Null(item.Category);
        Assert.Null(item.Priority);
        Assert.Null(item.IsDraftEligible);
        Assert.Null(item.ReviewDecision);
        Assert.Empty(item.FailedGates);
        Assert.Equal(DraftStates.None, item.DraftState);
        Assert.False(item.IsResolved);
    }

    /// The distinction ADR-012 keeps: a draft that failed citation validation
    /// is not the same as no draft, and collapsing them would hide it.
    [Fact]
    public async Task ADraftThatFailedCitationValidation_IsDistinctFromNoDraft()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            var source = AddTicket(db, "queue-failed-source");
            source.Resolve("A resolution.");

            ticketId = AddTicket(db, "queue-failed-draft").Id;
            await db.SaveChangesAsync();

            AddDraft(db, ticketId, source.Id, citationsValid: false);
            await db.SaveChangesAsync();
        }

        var item = await QueueItemAsync(ticketId);

        Assert.Equal(DraftStates.Failed, item.DraftState);
    }

    /// ADR-003: a gated ticket says which gate failed, not a number.
    [Fact]
    public async Task AGatedTicket_NamesTheGatesThatFailed()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            ticketId = AddTicket(db, "queue-gated").Id;
            AddRouting(db, ticketId, score: 0.1, similarity: 0.05);
            await db.SaveChangesAsync();
        }

        var item = await QueueItemAsync(ticketId);

        Assert.False(item.IsDraftEligible);
        Assert.Equal(
            [nameof(RoutingGate.ClassificationScore), nameof(RoutingGate.RetrievalSimilarity)],
            item.FailedGates.OrderBy(gate => gate));
    }

    /// A reviewer reads draft text in the detail view, never in the queue.
    /// Shipping it per row would waste bandwidth and, for a draft that failed
    /// validation, would serve text ADR-010 says to withhold.
    [Fact]
    public async Task TheQueueNeverCarriesDraftText()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            var source = AddTicket(db, "queue-text-source");
            source.Resolve("A resolution.");

            ticketId = AddTicket(db, "queue-text").Id;
            await db.SaveChangesAsync();

            AddDraft(db, ticketId, source.Id, citationsValid: true);
            await db.SaveChangesAsync();
        }

        var json = await _client.GetStringAsync($"/tickets?pageSize={MaxPageSize}");

        // Asserted first so the test cannot pass vacuously: now that the
        // queue is paged, a drafted ticket that simply fell off the page
        // would satisfy the real assertion below without proving anything.
        Assert.Contains(ticketId.ToString(), json);
        Assert.DoesNotContain(DraftText, json);
    }

    /// The property no behavioural test can see: the rows come back in one
    /// database round trip, not one per ticket per signal.
    ///
    /// Paging makes this two statements rather than one — a COUNT and the
    /// windowed projection — which amends ADR-012's "one projection query".
    /// What that entry was protecting is intact and is what this asserts: the
    /// statement count is fixed, and does not grow with tickets or signals.
    [Fact]
    public async Task TheQueueIsOneRoundTripForTheRowsAndOneForTheCount()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            var source = AddTicket(db, "queue-trip-source");
            source.Resolve("A resolution.");

            var first = AddTicket(db, "queue-trip-one").Id;
            var second = AddTicket(db, "queue-trip-two").Id;
            AddClassification(db, first);
            AddRouting(db, first, 0.95, 0.9);
            AddRouting(db, second, 0.1, 0.1);
            await db.SaveChangesAsync();

            AddDraft(db, first, source.Id, citationsValid: true);
            await db.SaveChangesAsync();
        }

        factory.Sql.Clear();
        await _client.GetStringAsync("/tickets");

        var queries = factory.Sql.Commands
            .Where(c => c.Contains("FROM \"Tickets\""))
            .ToList();

        Assert.Equal(2, queries.Count);

        // All four signals resolved inside the one projection statement.
        var projection = Assert.Single(queries, q => q.Contains("TicketClassifications"));

        Assert.Contains("TicketRoutingDecisions", projection);
        Assert.Contains("TicketDrafts", projection);
        Assert.Contains("TicketReviews", projection);
    }

    /// "Page in the database, not in memory" as something a test can see.
    ///
    /// Page two specifically: EF omits OFFSET when the skip is zero, so
    /// asking for page one would prove only half of it.
    [Fact]
    public async Task TheQueueIsPagedInTheDatabaseRatherThanInMemory()
    {
        factory.Sql.Clear();
        await _client.GetStringAsync("/tickets?page=2&pageSize=5");

        var projection = Assert.Single(
            factory.Sql.Commands, c => c.Contains("TicketClassifications"));

        Assert.Contains("LIMIT", projection);
        Assert.Contains("OFFSET", projection);
    }

    [Fact]
    public async Task TheQueueDefaultsToTenRowsPerPage()
    {
        // Ten of our own guarantees a full page regardless of what the other
        // five classes sharing this database have left behind.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            for (var i = 0; i < 10; i++)
            {
                AddTicket(db, $"queue-default-{i}");
            }

            await db.SaveChangesAsync();
        }

        var response = (await _client.GetFromJsonAsync<TicketQueuePageResponse>("/tickets"))!;

        Assert.Equal(1, response.Page);
        Assert.Equal(10, response.PageSize);
        Assert.Equal(10, response.Items.Count);
        Assert.True(response.TotalCount >= 10);
    }

    /// Clamped rather than rejected, and the envelope reports what was
    /// applied — which is the only way a caller finds out it did not get
    /// what it asked for.
    [Theory]
    [InlineData(1000, MaxPageSize)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public async Task APageSizeOutsideTheAllowedRangeIsClamped(int requested, int applied)
    {
        var response = (await _client.GetFromJsonAsync<TicketQueuePageResponse>(
            $"/tickets?pageSize={requested}"))!;

        Assert.Equal(applied, response.PageSize);
        Assert.True(response.Items.Count <= applied);
    }

    [Fact]
    public async Task APageBeyondTheEndIsEmptyAndStillReportsTheTotal()
    {
        var response = (await _client.GetFromJsonAsync<TicketQueuePageResponse>(
            "/tickets?page=100000&pageSize=10"))!;

        Assert.Empty(response.Items);
        Assert.Equal(100_000, response.Page);
        Assert.True(response.TotalCount > 0);
    }

    /// A page number large enough to overflow (page - 1) * pageSize as an
    /// int. Without the long arithmetic this is a negative OFFSET, which
    /// PostgreSQL rejects — a 500 on a hand-typed URL rather than an empty
    /// page.
    [Fact]
    public async Task APageNumberLargeEnoughToOverflowIsStillAnEmptyPage()
    {
        var response = await _client.GetAsync($"/tickets?page={int.MaxValue}&pageSize={MaxPageSize}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<TicketQueuePageResponse>();

        Assert.Empty(page!.Items);
    }

    /// The defect offset paging has whenever its sort is not a total order.
    ///
    /// CreatedAt is truncated to microseconds, so ties are genuinely
    /// reachable — the seeded evaluation dataset is full of them, because 300
    /// tickets are ingested in a tight loop. With no tie-break PostgreSQL is
    /// free to order tied rows differently for each query, and rows then
    /// repeat on one page while vanishing from another.
    ///
    /// Forced through raw SQL rather than by opening up the domain: CreatedAt
    /// is `private init` and should not gain a setter for a test's benefit.
    [Fact]
    public async Task TiedCreatedAtTimestampsStillPageWithoutRepeatingOrLosingRows()
    {
        var marker = $"queue-tie-{Guid.CreateVersion7()}";
        var seeded = new List<Guid>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            for (var i = 0; i < 6; i++)
            {
                seeded.Add(AddTicket(db, $"{marker}-{i}").Id);
            }

            await db.SaveChangesAsync();

            // One timestamp for all six, far enough ahead that they sit at
            // the top of the queue whatever else is in the database.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "Tickets" SET "CreatedAt" = {DateTimeOffset.UtcNow.AddYears(1)} WHERE "Subject" LIKE {marker + "%"}""");
        }

        var first = await QueuePageAsync(page: 1, pageSize: 3);
        var second = await QueuePageAsync(page: 2, pageSize: 3);

        var returned = first.Items.Concat(second.Items).Select(item => item.Id).ToList();

        Assert.Equal(6, returned.Count);
        Assert.Equal(6, returned.Distinct().Count());
        Assert.Equal(seeded.OrderBy(id => id), returned.OrderBy(id => id));
    }
}
