using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Generation;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;
using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Infrastructure.Ai;

public enum DraftOutcome
{
    /// A draft row was written. Check <see cref="TicketDraft.CitationsValid"/>
    /// before showing anything to a reviewer.
    Created,

    /// A draft already exists. Generation is idempotent per ticket.
    AlreadyExists,

    /// The routing gate sent this ticket to manual triage, or nothing was
    /// retrievable to ground a draft on.
    NotEligible,

    NotConfigured,
    Failed,
}

public sealed record DraftGenerationResult(DraftOutcome Outcome, TicketDraft? Draft);

public sealed class TicketDraftService(
    IChatClient? chatClient,
    string? chatModel,
    PiiRedactor redactor,
    SupportTicketTriageDbContext db,
    TicketRetrievalService retrieval,
    ILogger<TicketDraftService> logger)
{
    /// Generates a grounded draft for a ticket that cleared both routing gates.
    ///
    /// Unlike embedding and classification this is not part of ingest: it is
    /// the most expensive call in the system and generating for tickets nobody
    /// opens is wasted money, so a reviewer's request drives it.
    public async Task<DraftGenerationResult> GenerateAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (chatClient is null || chatModel is null)
        {
            return new DraftGenerationResult(DraftOutcome.NotConfigured, null);
        }

        var existing = await db.TicketDrafts
            .Include(d => d.Sources)
            .Where(d => d.TicketId == ticket.Id)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return new DraftGenerationResult(DraftOutcome.AlreadyExists, existing);
        }

        // The gate is load-bearing rather than decorative: a ticket routed to
        // manual triage does not get a draft, whatever the caller asks for.
        var routing = await db.TicketRoutingDecisions
            .Where(d => d.TicketId == ticket.Id)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (routing is null || !routing.IsDraftEligible)
        {
            return new DraftGenerationResult(DraftOutcome.NotEligible, null);
        }

        var similar = await retrieval.FindSimilarAsync(ticket.Id, ct: ct);
        if (similar.Count == 0)
        {
            // The gate passed earlier but the corpus has since changed. No
            // sources means no grounded draft is possible.
            logger.LogWarning("No sources retrievable for ticket {TicketId}; no draft generated.", ticket.Id);
            return new DraftGenerationResult(DraftOutcome.NotEligible, null);
        }

        var redactedTicket = redactor.Redact($"{ticket.Subject}\n\n{ticket.Body}");

        // Resolutions are written by staff and are not redacted anywhere else
        // in the system, but they become grounding context here - and the
        // specification allows only redacted content to reach the model. The
        // retrieved body text is already redacted; the resolution is not.
        var sources = similar
            .Select(s => new GenerationPrompt.Source(
                s.TicketId, s.RedactedText, redactor.Redact(s.Resolution)))
            .ToList();

        var retrievalSet = similar
            .Select((s, index) => new RetrievedSource(s.TicketId, index + 1, s.Similarity))
            .ToList();

        var delimiter = GenerationPrompt.NewDelimiter();

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, GenerationPrompt.System(delimiter)),
                    new ChatMessage(ChatRole.User, GenerationPrompt.User(delimiter, redactedTicket, sources)),
                ],
                new ChatOptions { Temperature = 0f, ResponseFormat = ChatResponseFormat.Json },
                ct);

            if (!DraftResult.TryParse(response.Text, out var result, out var failure))
            {
                logger.LogWarning("Could not parse a draft for ticket {TicketId}: {Failure}", ticket.Id, failure);
                return new DraftGenerationResult(DraftOutcome.Failed, null);
            }

            var citations = CitationValidation.Validate(
                result!.CitedTicketIds,
                retrievalSet.Select(s => s.TicketId).ToHashSet());

            if (!citations.AllValid)
            {
                // Recorded, not discarded, and never served. A model citing
                // evidence it was not given is the signal worth keeping.
                logger.LogWarning(
                    "Draft for ticket {TicketId} cited {InvalidCount} id(s) outside its retrieval set.",
                    ticket.Id,
                    citations.Invalid.Count);
            }

            var draft = TicketDraft.Create(
                ticket.Id,
                result.DraftText,
                retrievalSet,
                citations,
                chatModel,
                GenerationPrompt.Version,
                PiiRedactor.Version);

            db.TicketDrafts.Add(draft);
            await db.SaveChangesAsync(ct);

            return new DraftGenerationResult(DraftOutcome.Created, draft);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to generate a draft for ticket {TicketId}.", ticket.Id);
            return new DraftGenerationResult(DraftOutcome.Failed, null);
        }
    }
}
