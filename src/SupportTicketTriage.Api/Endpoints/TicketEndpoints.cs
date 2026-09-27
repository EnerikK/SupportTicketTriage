using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Routing;

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
        app.MapGet("/tickets/{id:guid}/routing", GetRouting);
        app.MapPost("/tickets/{id:guid}/draft", CreateDraft);
        app.MapGet("/tickets/{id:guid}/draft", GetDraft);
    }

    /// Generation is a reviewer action rather than part of ingest: it is the
    /// most expensive call in the system, and drafting for tickets nobody
    /// opens is wasted money. Idempotent per ticket — asking twice returns
    /// the draft that already exists rather than paying for a second one.
    private static async Task<Results<Created<TicketDraftResponse>, Ok<TicketDraftResponse>, NotFound, Conflict<string>, ProblemHttpResult>> CreateDraft(
        Guid id,
        SupportTicketTriageDbContext db,
        TicketDraftService drafts,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ticket is null)
        {
            return TypedResults.NotFound();
        }

        var result = await drafts.GenerateAsync(ticket, ct);

        return result.Outcome switch
        {
            DraftOutcome.Created =>
                TypedResults.Created(
                    $"/tickets/{id}/draft", TicketDraftResponse.FromEntity(result.Draft!)),

            DraftOutcome.AlreadyExists =>
                TypedResults.Ok(TicketDraftResponse.FromEntity(result.Draft!)),

            // The routing gate is load-bearing: a ticket sent to manual triage
            // does not get a draft however the caller asks.
            DraftOutcome.NotEligible =>
                TypedResults.Conflict(
                    "This ticket was routed for manual triage, so no draft is generated."),

            DraftOutcome.NotConfigured =>
                TypedResults.Problem(
                    "No Azure OpenAI chat deployment is configured.", statusCode: 503),

            _ => TypedResults.Problem("Draft generation failed.", statusCode: 502),
        };
    }

    private static async Task<Results<Ok<TicketDraftResponse>, NotFound>> GetDraft(
        Guid id,
        SupportTicketTriageDbContext db,
        CancellationToken ct)
    {
        var draft = await db.TicketDrafts
            .Include(d => d.Sources)
            .Where(d => d.TicketId == id)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return draft is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketDraftResponse.FromEntity(draft));
    }

    /// Returns the most recent routing decision, on the same 404-rather-than-
    /// empty-200 basis as the classification resource.
    private static async Task<Results<Ok<TicketRoutingResponse>, NotFound>> GetRouting(
        Guid id,
        SupportTicketTriageDbContext db,
        CancellationToken ct)
    {
        var decision = await db.TicketRoutingDecisions
            .Where(d => d.TicketId == id)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return decision is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketRoutingResponse.FromEntity(decision));
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
        TicketRoutingService routing,
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

        // Last, because it reads what the two above produced. Either one
        // missing is a failed gate rather than an error: a ticket the system
        // could not classify is exactly one a human should look at.
        await routing.TryEvaluateAsync(ticket, ct);

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
