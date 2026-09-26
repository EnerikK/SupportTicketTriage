using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Api.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this WebApplication app)
    {
        app.MapPost("/tickets", IngestTicket);
        app.MapGet("/tickets", ListTickets);
        app.MapGet("/tickets/{id:guid}", GetTicket);
        app.MapGet("/tickets/{id:guid}/similar", GetSimilarTickets);
        app.MapGet("/tickets/{id:guid}/classification", GetClassification);
    }

    /// Returns the most recent classification. A ticket that exists but has
    /// never been classified is a 404 on this resource rather than an empty
    /// 200, so "not classified" and "classified" are never confused.
    private static async Task<Results<Ok<TicketClassificationResponse>, NotFound>> GetClassification(
        Guid id,
        SupportTicketTriageDbContext db,
        CancellationToken ct)
    {
        var classification = await db.TicketClassifications
            .Where(c => c.TicketId == id)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return classification is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketClassificationResponse.FromEntity(classification));
    }

    private static async Task<Results<Ok<List<SimilarTicketResponse>>, NotFound>> GetSimilarTickets(
        Guid id,
        SupportTicketTriageDbContext db,
        TicketRetrievalService retrieval,
        CancellationToken ct)
    {
        if (!await db.Tickets.AnyAsync(t => t.Id == id, ct))
        {
            return TypedResults.NotFound();
        }

        var similar = await retrieval.FindSimilarAsync(id, ct: ct);

        return TypedResults.Ok(similar.Select(SimilarTicketResponse.FromResult).ToList());
    }

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> IngestTicket(
        IngestTicketRequest request,
        SupportTicketTriageDbContext db,
        TicketEmbeddingService embeddings,
        TicketClassificationService classification,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Subject and Body are required."],
            });
        }

        var ticket = Ticket.Create(request.Subject, request.Body);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(ct);

        // Deliberately after the ticket is persisted, and deliberately not
        // allowed to fail the request: ingest must survive Azure being down.
        // Both stages record their own failure and neither blocks the other.
        await embeddings.TryEmbedAsync(ticket, ct);
        await classification.TryClassifyAsync(ticket, ct);

        var response = TicketResponse.FromDomain(ticket);
        return TypedResults.Created($"/tickets/{ticket.Id}", response);
    }

    private static async Task<Ok<List<TicketResponse>>> ListTickets(SupportTicketTriageDbContext db, CancellationToken ct)
    {
        var tickets = await db.Tickets
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(tickets.Select(TicketResponse.FromDomain).ToList());
    }

    private static async Task<Results<Ok<TicketResponse>, NotFound>> GetTicket(
        Guid id,
        SupportTicketTriageDbContext db,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FindAsync([id], ct);

        return ticket is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketResponse.FromDomain(ticket));
    }
}
