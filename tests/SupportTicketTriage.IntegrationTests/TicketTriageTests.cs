using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

/// Triage after ingest.
///
/// The case these exist for is not hypothetical: §8.8 of the project notes
/// records 4 classification failures in 300 ingests, each already past four
/// Azure SDK retries. Before this endpoint those tickets were unreachable by
/// any interface — classification and routing ran only inside ingest, and
/// nothing could re-run them.
[Collection(nameof(ClassificationApiCollection))]
public class TicketTriageTests(ClassificationApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<Guid> IngestAsync(string subject, bool classificationFails)
    {
        factory.ChatClient.Reset();
        factory.ChatClient.ShouldThrow = classificationFails;

        var response = await _client.PostAsJsonAsync(
            "/tickets", new IngestTicketRequest(subject, $"Body of {subject}."));

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<TicketResponse>();

        factory.ChatClient.ShouldThrow = false;
        return created!.Id;
    }

    /// The whole point of the endpoint. A ticket whose classification call
    /// failed at ingest arrives with nothing, and used to stay that way.
    [Fact]
    public async Task ATicketThatFailedClassificationAtIngest_CanBeTriagedAfterwards()
    {
        var id = await IngestAsync("triage-recovers", classificationFails: true);

        // Precondition, asserted rather than assumed — and it is not the shape
        // you would guess. Ingest runs routing regardless, because a ticket it
        // could not classify is exactly one a human should look at, so the
        // stuck ticket has NO classification but DOES have a routing decision
        // with its gates failed. "No routing" is therefore the wrong signal
        // for "triage did not finish"; absent classification is the right one.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/tickets/{id}/classification")).StatusCode);

        var before = await _client.GetFromJsonAsync<TicketRoutingResponse>($"/tickets/{id}/routing");
        Assert.False(before!.IsDraftEligible);

        var triage = await _client.PostAsync($"/tickets/{id}/triage", null);

        Assert.Equal(HttpStatusCode.OK, triage.StatusCode);
        Assert.NotNull(await triage.Content.ReadFromJsonAsync<TicketRoutingResponse>());

        // The gap that is actually closed: the ticket now has the
        // classification it could never previously acquire.
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/tickets/{id}/classification")).StatusCode);
    }

    /// ADR-008 stores a versioned classification row per run with
    /// most-recent-wins semantics, which is what makes re-running after a
    /// prompt change a supported operation rather than a workaround.
    [Fact]
    public async Task TriagingTwice_RecordsASecondClassificationRatherThanRefusing()
    {
        var id = await IngestAsync("triage-reruns", classificationFails: false);

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/tickets/{id}/triage", null)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        // One from ingest, one from the triage call.
        Assert.Equal(2, await db.TicketClassifications.CountAsync(c => c.TicketId == id));
    }

    [Fact]
    public async Task TriagingATicketThatDoesNotExist_Is404()
    {
        var response = await _client.PostAsync($"/tickets/{Guid.CreateVersion7()}/triage", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// A recorded decision sits on top of the routing that produced the draft
    /// it approved. Re-routing underneath it would leave the review pointing
    /// at reasoning that no longer exists, and ADR-011 makes a review final.
    [Fact]
    public async Task TriagingAnAlreadyReviewedTicket_IsRefused()
    {
        var id = await IngestAsync("triage-reviewed", classificationFails: false);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

            // A real resolved ticket: TicketDraftSources carries a foreign key
            // to Tickets, so an invented source id is rejected by the database
            // rather than quietly stored.
            var source = Ticket.Create("triage-reviewed-source", "Body of the source.");
            source.Resolve("A resolution.");
            db.Tickets.Add(source);
            await db.SaveChangesAsync();

            var retrievalSet = new[] { new RetrievedSource(source.Id, 1, 0.9) };
            var draft = TicketDraft.Create(
                id,
                "A draft.",
                retrievalSet,
                CitationValidation.Validate([source.Id], retrievalSet.Select(s => s.TicketId).ToHashSet()),
                ClassificationApiFactory.ChatDeployment,
                "v1",
                "v1");

            db.TicketDrafts.Add(draft);
            await db.SaveChangesAsync();

            db.TicketReviews.Add(TicketReview.Create(
                id,
                draft.Id,
                new ReviewSubmission(ReviewDecision.Approved, "The agreed reply.", WasEdited: false, null)));

            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsync($"/tickets/{id}/triage", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already been reviewed", await response.Content.ReadAsStringAsync());
    }
}
