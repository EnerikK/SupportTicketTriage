using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Api.Contracts;

public sealed record SimilarTicketResponse(
    Guid TicketId,
    string Subject,
    string RedactedText,
    string Resolution,
    double Similarity)
{
    public static SimilarTicketResponse FromResult(SimilarTicket result) =>
        new(result.TicketId, result.Subject, result.RedactedText, result.Resolution, result.Similarity);
}
