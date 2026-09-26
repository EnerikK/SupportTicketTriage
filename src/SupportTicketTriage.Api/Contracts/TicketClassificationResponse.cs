using SupportTicketTriage.Domain;
using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Api.Contracts;

public sealed record TicketClassificationResponse(
    string Category,
    string Priority,
    double SelfReportedScore,
    string ChatModel,
    string PromptVersion,
    DateTimeOffset CreatedAt)
{
    public static TicketClassificationResponse FromEntity(TicketClassification classification) =>
        new(
            // The label rather than the enum member name, so the wire format
            // matches the evaluation dataset's vocabulary exactly.
            classification.Category.ToLabel(),
            classification.Priority.ToString(),
            classification.SelfReportedScore,
            classification.ChatModel,
            classification.PromptVersion,
            classification.CreatedAt);
}
