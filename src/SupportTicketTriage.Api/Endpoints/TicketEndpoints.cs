using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SupportTicketTriage.Api.Contracts;
using SupportTicketTriage.Application.Routing;
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
        app.MapGet("/categories", ListCategories);
        app.MapGet("/tickets/{id:guid}", GetTicket);
        app.MapGet("/tickets/{id:guid}/similar", GetSimilarTickets);
        app.MapGet("/tickets/{id:guid}/classification", GetClassification);
        app.MapGet("/tickets/{id:guid}/routing", GetRouting);
        app.MapPost("/tickets/{id:guid}/triage", TriageTicket);
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

            DraftOutcome.AlreadyReviewed =>
                TypedResults.Conflict("This ticket has already been reviewed, so no draft is generated."),

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
    /// All four review actions are this one endpoint. Approve, edit and
    /// approve, and reject judge a draft; resolve manually is for a ticket
    /// with no draft that can be judged, and carries the reviewer's own reply
    /// in FinalText. They differ by what the reviewer submits, not by which
    /// verb they reach for. Nothing sends anything.
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
            var value when string.Equals(value, nameof(ReviewDecision.ResolvedManually), StringComparison.OrdinalIgnoreCase)
                => ReviewDecision.ResolvedManually,
            _ => null,
        };

        if (decision is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["decision"] = ["Decision must be one of Approved, Rejected or ResolvedManually."],
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

            ReviewOutcome.ReviewableDraftExists =>
                TypedResults.Conflict("This ticket has a validated draft; approve, edit or reject it instead."),

            ReviewOutcome.AlreadyResolved =>
                TypedResults.Conflict("This ticket is already resolved."),

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

    /// Runs the triage stages against a ticket that already exists.
    ///
    /// Until this existed, embedding, classification and routing happened
    /// **only** inside ingest, and a ticket that missed any of them was stuck
    /// for good: §8.8 records 4 classification failures in 300 calls, each
    /// already past four Azure SDK retries, and every one of those tickets was
    /// unreachable by any interface. A corpus that develops holes and offers
    /// no way to fill them is the gap this closes.
    ///
    /// The same three calls ingest makes, in the same order and for the same
    /// reasons — routing last, because it reads what the other two produced.
    /// Embedding is skipped when the ticket already has one for the current
    /// model; classification deliberately is not, because ADR-008 stores a
    /// versioned row per run with most-recent-wins semantics, which makes
    /// re-running after a prompt change a supported operation rather than a
    /// workaround. The client only offers this on a ticket with no routing
    /// decision, so the paid re-run is never one stray click away.
    ///
    /// Not a reversal of ADR-010. That entry made *generation* an on-demand
    /// reviewer action because it is the most expensive call in the system.
    /// Classification is roughly a fifth of the cost and gates everything
    /// downstream of it, so the bar for letting a human trigger it is lower.
    private static async Task<Results<Ok<TicketRoutingResponse>, NotFound, Conflict<string>>> TriageTicket(
        Guid id,
        SupportTicketTriageDbContext db,
        TicketEmbeddingService embeddings,
        TicketClassificationService classification,
        TicketRoutingService routing,
        CancellationToken ct)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);

        if (ticket is null)
        {
            return TypedResults.NotFound();
        }

        // A recorded human decision sits on top of the routing that produced
        // the draft it approved. Re-routing underneath it would leave the
        // review pointing at reasoning that no longer exists, and ADR-011
        // makes a review final.
        if (await db.TicketReviews.AnyAsync(r => r.TicketId == id, ct))
        {
            return TypedResults.Conflict("This ticket has already been reviewed; its triage is final.");
        }

        await embeddings.TryEmbedAsync(ticket, ct);
        await classification.TryClassifyAsync(ticket, ct);
        await routing.TryEvaluateAsync(ticket, ct);

        // The routing decision is the answer worth returning: it is what the
        // caller is waiting to see, and a failed gate is a result rather than
        // an error. Absent means every stage failed, which is reported as
        // such rather than as a success with nothing in it.
        var decision = await db.TicketRoutingDecisions
            .Where(r => r.TicketId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return decision is null
            ? TypedResults.Conflict("Triage produced no routing decision; the AI boundary is unavailable.")
            : TypedResults.Ok(TicketRoutingResponse.FromEntity(decision));
    }

    /// Default and ceiling for the queue page size.
    ///
    /// The ceiling exists because the page size arrives from the client and a
    /// request for every row is the unbounded query this endpoint was paged to
    /// get rid of. Ten is the default because the queue is a work list, not a
    /// report — a reviewer works the top of it.
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;

    /// The review queue, one page at a time.
    ///
    /// Composed server-side rather than returning bare tickets and letting the
    /// UI fetch classification, routing, draft and review per row: at fifty
    /// tickets that is two hundred requests to render one screen.
    ///
    /// Each signal is append-only with most-recent-wins semantics. EF reads
    /// the returned signals through one window-function join each, but writes
    /// the state's CASE as correlated LIMIT 1 subqueries, some of them
    /// repeated. PostgreSQL evaluates those after the sort, for each row up
    /// to the end of the requested page (EXPLAIN ANALYZE, 2026-11-22), so a
    /// deep page pays for the rows its OFFSET skips. Fine at this size; past
    /// a few thousand tickets, ADR-012's denormalised state columns are the
    /// fix.
    /// It is still one round trip for the page, and an integration test
    /// asserts that by reading the SQL EF Core actually sends.
    ///
    /// Paging makes this two statements rather than one: a COUNT and a
    /// windowed projection. ADR-012 recorded the queue as "one projection
    /// query" and this amends it — the property worth protecting was never
    /// "exactly one statement", it was "not one query per ticket per signal",
    /// and that still holds.
    ///
    /// The priority and state filters apply before the count and the paging
    /// (ADR-047), so totalCount is the filtered total and a page is a page of
    /// matches, not a page of tickets with the non-matching ones removed.
    private static async Task<Results<Ok<TicketQueuePageResponse>, ValidationProblem>> ListTickets(
        SupportTicketTriageDbContext db,
        CancellationToken ct,
        int page = 1,
        int pageSize = DefaultPageSize,
        string? priority = null,
        string? state = null,
        string? category = null)
    {
        // Clamped rather than rejected. A page size of 500 is a client bug or
        // somebody experimenting in the address bar, and answering it with a
        // hundred rows is more useful than a 400. The envelope reports what
        // was actually applied, so nobody is left guessing which they got.
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // A filter, unlike a page size, is rejected rather than corrected:
        // there is no nearest priority to "Urgent", and answering with the
        // whole queue would look like a filter that matched everything.
        var errors = new Dictionary<string, string[]>();

        TicketPriority? priorityFilter = null;
        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (TryMatchName<TicketPriority>(priority, out var matched))
            {
                priorityFilter = matched;
            }
            else
            {
                errors["priority"] = [$"Priority must be one of {string.Join(", ", Enum.GetNames<TicketPriority>())}."];
            }
        }

        // By label, never the enum member name: TryFromLabel refuses
        // "AccountAndLogin" so that one category has one spelling (ADR-008).
        TicketCategory? categoryFilter = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (TicketCategories.TryFromLabel(category, out var matched))
            {
                categoryFilter = matched;
            }
            else
            {
                errors["category"] = [$"Category must be one of {string.Join(", ", TicketCategories.Labels)}."];
            }
        }

        QueueState? stateFilter = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (TryMatchName<QueueState>(state, out var matched))
            {
                stateFilter = matched;
            }
            else
            {
                errors["state"] = [$"State must be one of {string.Join(", ", Enum.GetNames<QueueState>())}."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var tickets = db.Tickets
            .Select(t => new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                t.Resolution,
                Classification = db.TicketClassifications
                    .Where(c => c.TicketId == t.Id)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new { c.Category, c.Priority })
                    .FirstOrDefault(),
                Routing = db.TicketRoutingDecisions
                    .Where(r => r.TicketId == t.Id)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new { r.IsDraftEligible, r.FailedGates })
                    .FirstOrDefault(),
                // Only the validity flag, never the text: the queue has no
                // business carrying a draft body it will not render, and a
                // draft that failed validation must not leak one anywhere.
                DraftCitationsValid = db.TicketDrafts
                    .Where(d => d.TicketId == t.Id)
                    .OrderByDescending(d => d.CreatedAt)
                    .Select(d => (bool?)d.CitationsValid)
                    .FirstOrDefault(),
                Review = db.TicketReviews
                    .Where(v => v.TicketId == t.Id)
                    .Select(v => new { v.Decision })
                    .FirstOrDefault(),
            })
            // The state, as one conditional chain that EF writes as a SQL
            // CASE. Order is the point: a decision already taken outranks the
            // draft that produced it, and a draft that failed citation
            // validation is called out rather than folded into "no draft",
            // because ADR-010 withholds its content and a reviewer needs to
            // know why there is nothing to read. A ticket with no routing
            // decision was never evaluated (ADR-028).
            .Select(r => new
            {
                r.Id,
                r.Subject,
                r.CreatedAt,
                r.Resolution,
                r.Classification,
                r.Routing,
                r.DraftCitationsValid,
                r.Review,
                State =
                    r.Review != null && r.Review.Decision == ReviewDecision.Approved ? QueueState.Approved
                    : r.Review != null && r.Review.Decision == ReviewDecision.ResolvedManually ? QueueState.ResolvedManually
                    : r.Review != null && r.Review.Decision == ReviewDecision.Rejected ? QueueState.Rejected
                    : r.DraftCitationsValid == true ? QueueState.DraftReady
                    : r.DraftCitationsValid == false ? QueueState.DraftWithheld
                    : r.Routing != null && !r.Routing.IsDraftEligible ? QueueState.ManualTriage
                    : r.Routing != null ? QueueState.ReadyToDraft
                    : QueueState.NotTriaged,
            });

        // The same State expression the column shows, not a second copy of
        // the rule. In the WHERE it runs for every ticket rather than only
        // the page, which is the cost ADR-047 accepts.
        if (priorityFilter is { } wantedPriority)
        {
            tickets = tickets.Where(r => r.Classification != null && r.Classification.Priority == wantedPriority);
        }

        if (categoryFilter is { } wantedCategory)
        {
            tickets = tickets.Where(r => r.Classification != null && r.Classification.Category == wantedCategory);
        }

        if (stateFilter is { } wantedState)
        {
            tickets = tickets.Where(r => r.State == wantedState);
        }

        var total = await tickets.CountAsync(ct);

        // Computed as long before it is cast back. (page - 1) * pageSize
        // overflows int for a large hand-typed page number, and the negative
        // OFFSET that follows is a 500 rather than an empty page.
        //
        // Past the end we answer with no rows and the true total rather than
        // clamping to the last page: clamping would hide that the caller
        // asked for something that does not exist, and the envelope already
        // carries everything needed to recover. It also skips a query whose
        // only possible answer is nothing.
        var skip = (long)(page - 1) * pageSize;

        if (skip >= total)
        {
            return TypedResults.Ok(new TicketQueuePageResponse([], total, page, pageSize));
        }

        var rows = await tickets
            // Id is the tie-break, and it is not decoration. CreatedAt is
            // truncated to microseconds, so ties are genuinely reachable —
            // the seeded dataset is full of them — and without a total order
            // rows repeat and vanish across page boundaries. Ordering and
            // windowing are composed into the query before ToListAsync, so
            // LIMIT and OFFSET reach PostgreSQL rather than the paging
            // happening here.
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(ct);

        // Mapped here rather than inside the projection because naming the
        // failed gates is a flags-enum walk that no provider can translate.
        var queue = rows
            .Select(r => TicketQueueItemResponse.Create(
                r.Id,
                r.Subject,
                r.CreatedAt,
                r.State,
                isResolved: r.Resolution is not null,
                r.Classification?.Category,
                r.Classification?.Priority,
                r.Routing?.IsDraftEligible,
                r.Routing?.FailedGates ?? RoutingGate.None,
                r.DraftCitationsValid,
                r.Review?.Decision))
            .ToList();

        return TypedResults.Ok(new TicketQueuePageResponse(queue, total, page, pageSize));
    }

    /// Matches an enum member by its name only, ignoring case and surrounding
    /// space, the stance SubmitReview takes on a decision. Enum.TryParse would
    /// also accept "1", and a number is not a priority anyone chose.
    /// The category labels, from the one place they live. The queue's filter
    /// offers these rather than a copy in TypeScript, the drift `28c8a78`
    /// removed from the dashboard.
    private static Ok<IReadOnlyList<string>> ListCategories() =>
        TypedResults.Ok(TicketCategories.Labels);

    private static bool TryMatchName<TEnum>(string value, out TEnum result)
        where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(value.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }

        result = default;
        return false;
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
