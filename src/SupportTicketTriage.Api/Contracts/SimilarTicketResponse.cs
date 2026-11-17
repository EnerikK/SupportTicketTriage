using SupportTicketTriage.Infrastructure.Retrieval;

namespace SupportTicketTriage.Api.Contracts;

/// A retrieved precedent as the reviewer sees it.
///
/// `ResolutionSource` is served so the reviewer can tell a reply that was
/// written from cited precedents and approved from one a reviewer wrote with
/// nothing the system can trace behind it. It is a string, as review decisions
/// are, so renumbering the enum can never change what a client reads.
public sealed record SimilarTicketResponse(
    Guid TicketId,
    string Subject,
    string RedactedText,
    string Resolution,
    double Similarity,
    string ResolutionSource)
{
    public static SimilarTicketResponse FromResult(SimilarTicket result) =>
        new(
            result.TicketId,
            result.Subject,
            result.RedactedText,
            result.Resolution,
            result.Similarity,
            result.ResolutionSource.ToString());
}
