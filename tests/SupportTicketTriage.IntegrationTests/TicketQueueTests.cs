using System.Net.Http.Json;
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

    private async Task<TicketQueueItemResponse> QueueItemAsync(Guid ticketId)
    {
        var queue = await _client.GetFromJsonAsync<List<TicketQueueItemResponse>>("/tickets");
        return Assert.Single(queue!, item => item.Id == ticketId);
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
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            var source = AddTicket(db, "queue-text-source");
            source.Resolve("A resolution.");

            var ticketId = AddTicket(db, "queue-text").Id;
            await db.SaveChangesAsync();

            AddDraft(db, ticketId, source.Id, citationsValid: true);
            await db.SaveChangesAsync();
        }

        var json = await _client.GetStringAsync("/tickets");

        Assert.DoesNotContain(DraftText, json);
    }

    /// The property no behavioural test can see: the queue is one database
    /// round trip, not one per ticket per signal.
    [Fact]
    public async Task TheQueueIsOneRoundTrip()
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

        var query = Assert.Single(queries);

        // All four signals resolved inside that one statement.
        Assert.Contains("TicketClassifications", query);
        Assert.Contains("TicketRoutingDecisions", query);
        Assert.Contains("TicketDrafts", query);
        Assert.Contains("TicketReviews", query);
    }
}
