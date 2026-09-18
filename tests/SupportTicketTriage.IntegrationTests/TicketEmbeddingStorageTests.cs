using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.IntegrationTests;

[Collection(nameof(TicketApiCollection))]
public class TicketEmbeddingStorageTests(TicketApiFactory factory)
{
    private static Vector VectorOf(params (int Index, float Value)[] components)
    {
        var values = new float[TicketEmbedding.Dimensions];
        foreach (var (index, value) in components)
        {
            values[index] = value;
        }

        return new Vector(values);
    }

    [Fact]
    public async Task Embedding_RoundTripsThroughPgvector()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        var ticket = Ticket.Create("Vector round trip", "body");
        db.Tickets.Add(ticket);

        var embedding = TicketEmbedding.Create(
            ticket.Id,
            "redacted body",
            "v1",
            "text-embedding-3-small",
            VectorOf((0, 0.25f), (1535, -0.5f)));
        db.TicketEmbeddings.Add(embedding);

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.TicketEmbeddings.SingleAsync(e => e.TicketId == ticket.Id);

        Assert.Equal(TicketEmbedding.Dimensions, stored.Embedding.Memory.Length);
        Assert.Equal(0.25f, stored.Embedding.Memory.Span[0]);
        Assert.Equal(-0.5f, stored.Embedding.Memory.Span[1535]);
        Assert.Equal("text-embedding-3-small", stored.EmbeddingModel);
        Assert.Equal("v1", stored.RedactionVersion);
        Assert.Equal("redacted body", stored.RedactedText);
    }

    [Fact]
    public async Task CosineDistance_TranslatesToSqlAndOrdersBySimilarity()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportTicketTriageDbContext>();

        var near = Ticket.Create("Near", "body");
        var far = Ticket.Create("Far", "body");
        db.Tickets.AddRange(near, far);

        var query = VectorOf((0, 1f));
        db.TicketEmbeddings.AddRange(
            TicketEmbedding.Create(near.Id, "near", "v1", "test-model", VectorOf((0, 1f))),
            TicketEmbedding.Create(far.Id, "far", "v1", "test-model", VectorOf((1, 1f))));

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Exercises the plugin's method translator, which is the part most
        // likely to break across an EF Core major version.
        var ordered = await db.TicketEmbeddings
            .Where(e => e.EmbeddingModel == "test-model")
            .OrderBy(e => e.Embedding.CosineDistance(query))
            .Select(e => e.RedactedText)
            .ToListAsync();

        Assert.Equal(["near", "far"], ordered);
    }
}
