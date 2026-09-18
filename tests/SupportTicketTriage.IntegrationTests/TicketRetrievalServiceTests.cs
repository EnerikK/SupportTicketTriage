using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.IntegrationTests;

[Collection(nameof(TicketApiCollection))]
public class TicketRetrievalServiceTests(TicketApiFactory factory)
{
    // Tests share one database, and retrieval filters by embedding model, so a
    // model name unique to each test isolates its corpus from the others.
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

    [Fact]
    public async Task FindSimilar_ReturnsResolvedTicketsNearestFirst()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        var model = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
        AddTicket(db, "near", model, VectorOf((0, 1f), (1, 0.1f)), "near resolution");
        AddTicket(db, "far", model, VectorOf((1, 1f)), "far resolution");
        await db.SaveChangesAsync();

        var results = await retrieval.FindSimilarAsync(query.Id);

        Assert.Equal(["near", "far"], results.Select(r => r.Subject));
        Assert.Equal("near resolution", results[0].Resolution);
        Assert.True(results[0].Similarity > results[1].Similarity);
    }

    [Fact]
    public async Task FindSimilar_ExcludesUnresolvedTickets()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        var model = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
        AddTicket(db, "unresolved-but-identical", model, VectorOf((0, 1f)), resolution: null);
        AddTicket(db, "resolved-but-distant", model, VectorOf((5, 1f)), "the only eligible one");
        await db.SaveChangesAsync();

        var results = await retrieval.FindSimilarAsync(query.Id);

        var single = Assert.Single(results);
        Assert.Equal("resolved-but-distant", single.Subject);
    }

    [Fact]
    public async Task FindSimilar_ExcludesVectorsFromAnotherEmbeddingModel()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        string model = NewModel(), otherModel = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
        AddTicket(db, "same-model", model, VectorOf((3, 1f)), "comparable");
        AddTicket(db, "other-model", otherModel, VectorOf((0, 1f)), "not comparable");
        await db.SaveChangesAsync();

        var results = await retrieval.FindSimilarAsync(query.Id);

        var single = Assert.Single(results);
        Assert.Equal("same-model", single.Subject);
    }

    [Fact]
    public async Task FindSimilar_ExcludesTheQueryTicketItself()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        var model = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), "this one is resolved too");
        await db.SaveChangesAsync();

        Assert.Empty(await retrieval.FindSimilarAsync(query.Id));
    }

    [Fact]
    public async Task FindSimilar_ReturnsAtMostK()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        var model = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
        for (var i = 0; i < 4; i++)
        {
            AddTicket(db, $"candidate-{i}", model, VectorOf((0, 1f - (i * 0.1f))), $"resolution {i}");
        }

        await db.SaveChangesAsync();

        Assert.Equal(2, (await retrieval.FindSimilarAsync(query.Id, k: 2)).Count);
        Assert.Equal(4, (await retrieval.FindSimilarAsync(query.Id)).Count);
    }

    [Fact]
    public async Task FindSimilar_ReturnsEmptyWhenTheTicketHasNoEmbedding()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();

        var ticket = Ticket.Create("no embedding", "body");
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        Assert.Empty(await retrieval.FindSimilarAsync(ticket.Id));
    }

    [Fact]
    public async Task FindSimilar_FiltersAndLimitsInTheDatabaseRatherThanInMemory()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
        var retrieval = scope.ServiceProvider.GetRequiredService<TicketRetrievalService>();
        var model = NewModel();

        var query = AddTicket(db, "query", model, VectorOf((0, 1f)), resolution: null);
        AddTicket(db, "candidate", model, VectorOf((0, 1f)), "resolution");
        await db.SaveChangesAsync();

        factory.Sql.Clear();
        await retrieval.FindSimilarAsync(query.Id, k: 3);

        // Pulling the corpus into memory would still pass the behavioural tests
        // above while scanning the whole table, so assert on the SQL itself.
        var search = Assert.Single(
            factory.Sql.Commands, c => c.Contains("TicketEmbeddings") && c.Contains("Tickets"));

        Assert.Contains("LIMIT", search);
        Assert.Contains("IS NOT NULL", search);
        Assert.Contains("EmbeddingModel", search);
        // pgvector's cosine distance operator: the ordering is computed by the
        // database, not by materialising vectors and sorting them here.
        Assert.Contains("<=>", search);
    }

    [Fact]
    public async Task SimilarEndpoint_ReturnsResultsForAKnownTicketAndNotFoundOtherwise()
    {
        var client = factory.CreateClient();
        Guid queryId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();
            var model = NewModel();

            var query = AddTicket(db, "endpoint-query", model, VectorOf((0, 1f)), resolution: null);
            AddTicket(db, "endpoint-match", model, VectorOf((0, 1f)), "endpoint resolution");
            await db.SaveChangesAsync();
            queryId = query.Id;
        }

        var results = await client.GetFromJsonAsync<List<SimilarTicketResponse>>($"/tickets/{queryId}/similar");

        var single = Assert.Single(results!);
        Assert.Equal("endpoint-match", single.Subject);
        Assert.Equal("endpoint resolution", single.Resolution);

        var missing = await client.GetAsync($"/tickets/{Guid.NewGuid()}/similar");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
