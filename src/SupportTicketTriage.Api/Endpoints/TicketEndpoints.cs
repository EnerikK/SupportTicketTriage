using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Review;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;
using SupportTicketTriage.Infrastructure.Review;
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
        app.MapPost("/tickets/{id:guid}/review", SubmitReview);
        app.MapGet("/tickets/{id:guid}/review", GetReview);
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

    /// Records a reviewer's decision.
    ///
    /// All three review actions are this one endpoint. Approve, edit and
    /// approve, and reject differ by what the reviewer submits, not by which
    /// verb they reach for — and there is no fourth action, because approving
    /// is the end of the line. Nothing sends anything.
    private static async Task<Results<Created<TicketReviewResponse>, NotFound, Conflict<string>, ValidationProblem>> SubmitReview(
        Guid id,
        SubmitReviewRequest request,
        TicketReviewService reviews,
        CancellationToken ct)
    {
        // Matched against the names themselves rather than parsed with
        // Enum.TryParse, which would also accept "1" and "2". The wire format
        // is a closed set of words, the same stance the domain takes on
        // category and priority labels.
        ReviewDecision? decision = request.Decision?.Trim() switch
        {
            var value when string.Equals(value, nameof(ReviewDecision.Approved), StringComparison.OrdinalIgnoreCase)
                => ReviewDecision.Approved,
            var value when string.Equals(value, nameof(ReviewDecision.Rejected), StringComparison.OrdinalIgnoreCase)
                => ReviewDecision.Rejected,
            _ => null,
        };

        if (decision is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["decision"] = ["Decision must be either Approved or Rejected."],
            });
        }

        var result = await reviews.SubmitAsync(id, decision.Value, request.FinalText, request.RejectionReason, ct);

        return result.Outcome switch
        {
            ReviewOutcome.Recorded =>
                TypedResults.Created($"/tickets/{id}/review", TicketReviewResponse.FromEntity(result.Review!)),

            ReviewOutcome.TicketNotFound => TypedResults.NotFound(),

            ReviewOutcome.AlreadyReviewed =>
                TypedResults.Conflict("This ticket has already been reviewed."),

            // A conflict rather than a 404: the ticket is real, it just has
            // nothing a reviewer is allowed to act on. A draft that failed
            // citation validation is withheld, not approved around.
            ReviewOutcome.NoReviewableDraft =>
                TypedResults.Conflict("This ticket has no draft whose citations were validated."),

            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["review"] = [result.Failure ?? "The review could not be recorded."],
            }),
        };
    }

    /// The one review this ticket has, if it has been reviewed. 404 rather
    /// than an empty 200, on the same basis as classification and routing.
    private static async Task<Results<Ok<TicketReviewResponse>, NotFound>> GetReview(
        Guid id,
        SupportTicketTriageDbContext db,
        CancellationToken ct)
    {
        var review = await db.TicketReviews.FirstOrDefaultAsync(r => r.TicketId == id, ct);

        return review is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TicketReviewResponse.FromEntity(review));
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
