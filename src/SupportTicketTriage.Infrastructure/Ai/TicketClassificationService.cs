using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SupportTicketTriage.Application.Classification;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Infrastructure.Ai;

public sealed class TicketClassificationService(
    IChatClient? chatClient,
    string? chatModel,
    PiiRedactor redactor,
    SupportTicketTriageDbContext db,
    ILogger<TicketClassificationService> logger)
{
    /// Classifies a ticket and stores the result. Returns false when no usable
    /// classification could be produced.
    ///
    /// Never throws for a model-side failure, for the same reason embedding
    /// does not: an Azure outage must not become a ticket-ingest outage. A
    /// ticket with no classification row is one that has not been classified,
    /// and ADR-003 routes it to manual triage rather than guessing.
    public async Task<bool> TryClassifyAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (chatClient is null || chatModel is null)
        {
            logger.LogInformation(
                "Azure OpenAI chat is not configured; ticket {TicketId} stored without a classification.",
                ticket.Id);
            return false;
        }

        // Only redacted text crosses the AI boundary, here as well as on the
        // embedding path. The specification requires redaction before every
        // model call, not only before embedding.
        var redacted = redactor.Redact($"{ticket.Subject}\n\n{ticket.Body}");
        var delimiter = ClassificationPrompt.NewDelimiter();

        try
        {
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, ClassificationPrompt.System(delimiter)),
                    new ChatMessage(ChatRole.User, ClassificationPrompt.User(delimiter, redacted)),
                ],
                new ChatOptions
                {
                    // The task is a fixed choice from a closed list, so there is
                    // nothing for sampling to contribute except variance between
                    // identical inputs, which would make the eval unreproducible.
                    Temperature = 0f,
                    ResponseFormat = ChatResponseFormat.Json,
                },
                ct);

            if (!ClassificationResult.TryParse(response.Text, out var result, out var failure))
            {
                // A rejected response is expected traffic, not an exception:
                // the model can return an unknown category or malformed JSON,
                // and both mean the same thing to the caller.
                logger.LogWarning(
                    "Could not classify ticket {TicketId}: {Failure}", ticket.Id, failure);
                return false;
            }

            db.TicketClassifications.Add(TicketClassification.Create(
                ticket.Id,
                result!.Category,
                result.Priority,
                result.SelfReportedScore,
                redacted,
                PiiRedactor.Version,
                chatModel,
                ClassificationPrompt.Version));

            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to classify ticket {TicketId}.", ticket.Id);
            return false;
        }
    }
}
