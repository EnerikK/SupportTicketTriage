using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Api.Endpoints;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this WebApplication app)
    {
        app.MapPost("/tickets", IngestTicket);
        app.MapGet("/tickets", ListTickets);
        app.MapGet("/tickets/{id:guid}", GetTicket);
    }

    private static async Task<Results<Created<TicketResponse>, ValidationProblem>> IngestTicket(
        IngestTicketRequest request,
        SupportTicketTriageDbContext db,
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
