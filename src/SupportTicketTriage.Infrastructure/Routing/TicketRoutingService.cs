using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Routing;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Infrastructure.Routing;

public sealed class TicketRoutingService(
    SupportTicketTriageDbContext db,
    TicketRetrievalService retrieval,
    RoutingThresholds thresholds,
    ILogger<TicketRoutingService> logger)
{
    /// Evaluates both gates for a ticket and records the outcome.
    ///
    /// Runs after classification and embedding because it consumes both. When
    /// either produced nothing, the corresponding gate fails on absence, which
    /// is the correct answer rather than an error: a ticket the system could
    /// not classify is exactly a ticket a human should look at.
    ///
    /// Swallows failures for the same reason the two AI stages do — this runs
    /// inside ingest, after the ticket is already persisted, and a fault here
    /// must not turn a stored ticket into a failed request.
    public async Task<bool> TryEvaluateAsync(Ticket ticket, CancellationToken ct = default)
    {
        try
        {
            var classificationScore = await db.TicketClassifications
                .Where(c => c.TicketId == ticket.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => (double?)c.SelfReportedScore)
                .FirstOrDefaultAsync(ct);

            // Only the nearest neighbour matters for Gate B: the question is
            // whether anything sufficiently similar exists at all, not how
            // many. Asking for k = 1 keeps the ingest-path query minimal.
            var nearest = await retrieval.FindSimilarAsync(ticket.Id, k: 1, ct);
            double? topSimilarity = nearest.Count > 0 ? nearest[0].Similarity : null;

            var decision = RoutingDecision.Evaluate(classificationScore, topSimilarity, thresholds);

            db.TicketRoutingDecisions.Add(TicketRoutingDecision.Create(ticket.Id, decision));
            await db.SaveChangesAsync(ct);

            if (!decision.IsDraftEligible)
            {
                logger.LogInformation(
                    "Ticket {TicketId} routed for manual triage; failed gates: {FailedGates}.",
                    ticket.Id,
                    decision.FailedGates);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to evaluate routing for ticket {TicketId}.", ticket.Id);
            return false;
        }
    }
}
