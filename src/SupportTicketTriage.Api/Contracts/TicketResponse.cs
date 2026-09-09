using SupportTicketTriage.Domain;

namespace SupportTicketTriage.Api.Contracts;

public sealed record TicketResponse(Guid Id, string Subject, string Body, DateTimeOffset CreatedAt)
{
    public static TicketResponse FromDomain(Ticket ticket) =>
        new(ticket.Id, ticket.Subject, ticket.Body, ticket.CreatedAt);
}
