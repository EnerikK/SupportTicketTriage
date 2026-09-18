using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

[Collection(nameof(EmbeddingApiCollection))]
public class TicketEmbeddingServiceTests(EmbeddingApiFactory factory)
{
    [Fact]
    public async Task Ingest_SendsOnlyRedactedTextAcrossTheAiBoundary()
    {
        factory.Generator.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tickets", new IngestTicketRequest(
            "Refund for order",
            "Email me at jane.doe@example.com or call 5551234567, card 4111111111111111."));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

        var sent = Assert.Single(factory.Generator.Received);
        Assert.DoesNotContain("jane.doe@example.com", sent);
        Assert.DoesNotContain("5551234567", sent);
        Assert.DoesNotContain("4111111111111111", sent);
        Assert.Contains("[EMAIL]", sent);
        Assert.Contains("[PHONE]", sent);
        Assert.Contains("[CARD]", sent);

        // The original is still available internally, unredacted.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var ticket = await db.Tickets.SingleAsync(t => t.Id == created!.Id);
        Assert.Contains("jane.doe@example.com", ticket.Body);
    }

    [Fact]
    public async Task Ingest_StampsEmbeddingWithModelAndRedactionVersion()
    {
        factory.Generator.Reset();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/tickets", new IngestTicketRequest("Stamping", "Plain body with no identifiers."));
        var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var stored = await db.TicketEmbeddings.SingleAsync(e => e.TicketId == created!.Id);

        Assert.Equal("test-embedding-model", stored.EmbeddingModel);
        Assert.Equal(PiiRedactor.Version, stored.RedactionVersion);
        Assert.Equal(TicketEmbedding.Dimensions, stored.Embedding.Memory.Length);
        Assert.Contains("Plain body with no identifiers.", stored.RedactedText);
    }

    [Fact]
    public async Task Ingest_SucceedsWhenTheEmbeddingCallFails()
    {
        factory.Generator.Reset();
        factory.Generator.ShouldThrow = true;
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
            // No embedding row is the record that this ticket still needs one.
            Assert.False(await db.TicketEmbeddings.AnyAsync(e => e.TicketId == created!.Id));
        }
        finally
        {
            factory.Generator.Reset();
        }
    }
}
