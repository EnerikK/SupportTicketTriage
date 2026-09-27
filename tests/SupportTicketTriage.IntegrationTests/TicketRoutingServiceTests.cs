using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Routing;

namespace SupportTicketTriage.IntegrationTests;

/// Seeds vectors and classifications directly, the same approach the
/// retrieval tests take, because both gate inputs need exact values. Going
/// through the fakes would give a zero vector and an undefined similarity.
[Collection(nameof(TicketApiCollection))]
public class TicketRoutingServiceTests(TicketApiFactory factory)
{
    // Retrieval filters by embedding model and the tests share one database,
    // so a per-test model name isolates each corpus.
    private static string NewModel() => $"model-{Guid.NewGuid():N}";

    private static Vector VectorOf(params (int Index, float Value)[] components)
    {
        var values = new float[TicketEmbedding.Dimensions];
        foreach (var (index, value) in components)
        {
            values[index] = value;
        }

        return new Vector(values);
    }

    private static Ticket AddTicket(
        SupportTicketTriageDbContext db, string subject, string model, Vector vector, string? resolution)
    {
        var ticket = Ticket.Create(subject, $"body of {subject}");
        if (resolution is not null)
        {
            ticket.Resolve(resolution);
        }

        db.Tickets.Add(ticket);
        db.TicketEmbeddings.Add(
            TicketEmbedding.Create(ticket.Id, $"redacted {subject}", "v1", model, vector));

        return ticket;
    }

    private static void AddClassification(SupportTicketTriageDbContext db, Guid ticketId, double score) =>
        db.TicketClassifications.Add(TicketClassification.Create(
            ticketId,
            TicketCategory.Billing,
            TicketPriority.Normal,
            score,
            "redacted",
            "v1",
            "test-chat-model",
            "classify-v1"));

    private async Task<TicketRoutingDecision> EvaluateAsync(
        Func<SupportTicketTriageDbContext, string, Ticket> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var routing = scope.ServiceProvider.GetRequiredService<TicketRoutingService>();

        var ticket = seed(db, NewModel());
        await db.SaveChangesAsync();

        Assert.True(await routing.TryEvaluateAsync(ticket));

        return await db.TicketRoutingDecisions.SingleAsync(d => d.TicketId == ticket.Id);
    }

    [Fact]
    public async Task BothGatesClear_TicketIsDraftEligible()
    {
        var decision = await EvaluateAsync((db, model) =>
        {
            var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
            AddTicket(db, "near", model, VectorOf((0, 1f), (1, 0.05f)), "a near resolution");
            AddClassification(db, query.Id, 0.90);
            return query;
        });

        Assert.True(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.None, decision.FailedGates);
        Assert.Equal(0.90, decision.ClassificationScore);
        Assert.True(decision.TopSimilarity > 0.9);
    }

    [Fact]
    public async Task LowClassificationScore_RoutesToManualTriage()
    {
        var decision = await EvaluateAsync((db, model) =>
        {
            var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
            AddTicket(db, "near", model, VectorOf((0, 1f), (1, 0.05f)), "a near resolution");
            AddClassification(db, query.Id, 0.20);
            return query;
        });

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.ClassificationScore, decision.FailedGates);
    }

    /// A confident classifier with nothing similar retrieved. The draft would
    /// have nothing to ground itself on, which is what Gate B exists to catch.
    [Fact]
    public async Task ConfidentButNothingSimilar_RoutesToManualTriage()
    {
        var decision = await EvaluateAsync((db, model) =>
        {
            var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
            AddTicket(db, "orthogonal", model, VectorOf((1, 1f)), "an unrelated resolution");
            AddClassification(db, query.Id, 0.99);
            return query;
        });

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.RetrievalSimilarity, decision.FailedGates);
        Assert.Equal(0.99, decision.ClassificationScore);
    }

    [Fact]
    public async Task NeverClassified_FailsGateAWithNoScoreRecorded()
    {
        var decision = await EvaluateAsync((db, model) =>
        {
            var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
            AddTicket(db, "near", model, VectorOf((0, 1f), (1, 0.05f)), "a near resolution");
            return query;
        });

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.ClassificationScore, decision.FailedGates);
        // Absent, not low - the two are different operational problems.
        Assert.Null(decision.ClassificationScore);
    }

    [Fact]
    public async Task NothingRetrievable_FailsGateBWithNoSimilarityRecorded()
    {
        var decision = await EvaluateAsync((db, model) =>
        {
            var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
            AddClassification(db, query.Id, 0.95);
            return query;
        });

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.RetrievalSimilarity, decision.FailedGates);
        Assert.Null(decision.TopSimilarity);
    }

    [Fact]
    public async Task NeitherInputPresent_FailsBothGates()
    {
        var decision = await EvaluateAsync((db, model) =>
            AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null));

        Assert.False(decision.IsDraftEligible);
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.ClassificationScore));
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.RetrievalSimilarity));
    }

    /// Without the thresholds on the row, a decision becomes unreadable the
    /// moment the configured floors move — which Phase 5 will do deliberately.
    [Fact]
    public async Task TheThresholdsUsed_AreStoredOnTheDecision()
    {
        var decision = await EvaluateAsync((db, model) =>
            AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null));

        Assert.Equal(RoutingThresholds.DefaultMinimumClassificationScore, decision.MinimumClassificationScore);
        Assert.Equal(RoutingThresholds.DefaultMinimumSimilarity, decision.MinimumSimilarity);
    }

    [Fact]
    public async Task Routing_IsExposedWithTheFailedGatesNamed()
    {
        Guid ticketId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            var routing = scope.ServiceProvider.GetRequiredService<TicketRoutingService>();

            var query = AddTicket(db, "query", NewModel(), VectorOf((0, 1f)), resolution: null);
            await db.SaveChangesAsync();
            await routing.TryEvaluateAsync(query);
            ticketId = query.Id;
        }

        var response = await factory.CreateClient()
            .GetFromJsonAsync<TicketRoutingResponse>($"/tickets/{ticketId}/routing");

        Assert.False(response!.IsDraftEligible);
        // ADR-003 requires a gated ticket to say which gate failed, not just
        // that it was gated.
        Assert.Contains(nameof(RoutingGate.ClassificationScore), response.FailedGates);
        Assert.Contains(nameof(RoutingGate.RetrievalSimilarity), response.FailedGates);
    }

    /// This factory configures no AI at all, so ingest produces neither a
    /// classification nor an embedding. A routing decision must still be
    /// recorded: the spec requires the stack to stay usable when Azure OpenAI
    /// is unavailable, and "route everything to a human" is what that means
    /// for triage.
    [Fact]
    public async Task Ingest_RecordsARoutingDecisionEvenWithNoAiConfigured()
    {
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync(
                "/tickets", new IngestTicketRequest("No AI configured", "Nothing should crash.")))
            .Content.ReadFromJsonAsync<TicketResponse>();

        var response = await client.GetFromJsonAsync<TicketRoutingResponse>(
            $"/tickets/{created!.Id}/routing");

        Assert.False(response!.IsDraftEligible);
        Assert.Equal(2, response.FailedGates.Count);
        Assert.Null(response.ClassificationScore);
        Assert.Null(response.TopSimilarity);
    }

    [Fact]
    public async Task Routing_IsNotFoundForATicketThatWasNeverEvaluated()
    {
        var response = await factory.CreateClient().GetAsync($"/tickets/{Guid.CreateVersion7()}/routing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
