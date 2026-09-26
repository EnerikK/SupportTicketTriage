using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

[Collection(nameof(ClassificationApiCollection))]
public class TicketClassificationServiceTests(ClassificationApiFactory factory)
{
    [Fact]
    public async Task Ingest_SendsOnlyRedactedTextAcrossTheAiBoundary()
    {
        factory.ChatClient.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets", new IngestTicketRequest(
            "Refund for order",
            "Email me at jane.doe@example.com or call 5551234567, card 4111111111111111, from 10.1.2.3."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var sent = factory.ChatClient.AllText;
        Assert.DoesNotContain("jane.doe@example.com", sent);
        Assert.DoesNotContain("5551234567", sent);
        Assert.DoesNotContain("4111111111111111", sent);
        Assert.DoesNotContain("10.1.2.3", sent);
        Assert.Contains("[EMAIL]", sent);
        Assert.Contains("[CARD]", sent);
        Assert.Contains("[IP]", sent);
    }

    /// ADR-002's structural separation: instructions live only in the system
    /// message and ticket content only in a user message. Asserted on message
    /// roles rather than on prompt wording, which would be brittle.
    [Fact]
    public async Task InstructionsAndTicketContent_TravelInSeparateMessages()
    {
        factory.ChatClient.Reset();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/tickets", new IngestTicketRequest(
            "Distinctive subject line", "Distinctive body sentence."));

        var messages = Assert.Single(factory.ChatClient.Received);
        Assert.Equal(2, messages.Count);

        var system = Assert.Single(messages, m => m.Role == ChatRole.System);
        var user = Assert.Single(messages, m => m.Role == ChatRole.User);

        Assert.DoesNotContain("Distinctive body sentence.", system.Text);
        Assert.Contains("Distinctive body sentence.", user.Text);
    }

    [Fact]
    public async Task Ingest_StoresTheClassificationWithItsProvenance()
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ResponseText =
            """{"category": "Shipping & Delivery", "priority": "Low", "score": 0.64}""";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/tickets", new IngestTicketRequest("Parcel", "Where is my order."));
        var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var stored = await db.TicketClassifications.SingleAsync(c => c.TicketId == created!.Id);

        Assert.Equal(TicketCategory.ShippingAndDelivery, stored.Category);
        Assert.Equal(TicketPriority.Low, stored.Priority);
        Assert.Equal(0.64, stored.SelfReportedScore);
        Assert.Equal(ClassificationApiFactory.ChatDeployment, stored.ChatModel);
        Assert.Equal(ClassificationPrompt.Version, stored.PromptVersion);
        Assert.Equal(PiiRedactor.Version, stored.RedactionVersion);
        Assert.Contains("Where is my order.", stored.RedactedText);
    }

    [Fact]
    public async Task Ingest_SucceedsWhenTheModelReturnsAnUnknownCategory()
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ResponseText =
            """{"category": "Warranty Claim", "priority": "Normal", "score": 0.99}""";
        var client = factory.CreateClient();

        try
        {
            var response = await client.PostAsJsonAsync(
                "/tickets", new IngestTicketRequest("Invented category", "The ticket must still be stored."));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            Assert.True(await db.Tickets.AnyAsync(t => t.Id == created!.Id));
            // No row rather than a guessed category: the ticket is unclassified
            // and routes to manual triage.
            Assert.False(await db.TicketClassifications.AnyAsync(c => c.TicketId == created!.Id));
        }
        finally
        {
            factory.ChatClient.Reset();
        }
    }

    [Fact]
    public async Task Ingest_SucceedsWhenTheChatCallFails()
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ShouldThrow = true;
        var client = factory.CreateClient();

        try
        {
            var response = await client.PostAsJsonAsync(
                "/tickets", new IngestTicketRequest("Azure is down", "The ticket must still be stored."));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            Assert.True(await db.Tickets.AnyAsync(t => t.Id == created!.Id));
            Assert.False(await db.TicketClassifications.AnyAsync(c => c.TicketId == created!.Id));
        }
        finally
        {
            factory.ChatClient.Reset();
        }
    }

    [Fact]
    public async Task Classification_IsExposedInTheDatasetsLabelForm()
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ResponseText =
            """{"category": "Account & Login", "priority": "High", "score": 0.77}""";
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync(
                "/tickets", new IngestTicketRequest("Cannot sign in", "Password reset never arrives.")))
            .Content.ReadFromJsonAsync<TicketResponse>();

        var classification = await client.GetFromJsonAsync<TicketClassificationResponse>(
            $"/tickets/{created!.Id}/classification");

        // The label, not the enum member name, so the wire format matches the
        // evaluation dataset exactly.
        Assert.Equal("Account & Login", classification!.Category);
        Assert.Equal("High", classification.Priority);
        Assert.Equal(0.77, classification.SelfReportedScore);
    }

    [Fact]
    public async Task Classification_IsNotFoundWhenTheTicketWasNeverClassified()
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ShouldThrow = true;
        var client = factory.CreateClient();

        try
        {
            var created = await (await client.PostAsJsonAsync(
                    "/tickets", new IngestTicketRequest("Unclassified", "No classification for this one.")))
                .Content.ReadFromJsonAsync<TicketResponse>();

            var response = await client.GetAsync($"/tickets/{created!.Id}/classification");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            factory.ChatClient.Reset();
        }
    }

    [Fact]
    public async Task Classification_IsNotFoundForAnUnknownTicket()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/tickets/{Guid.CreateVersion7()}/classification");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
