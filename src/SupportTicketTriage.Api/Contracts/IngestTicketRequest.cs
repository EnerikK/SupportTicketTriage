namespace SupportTicketTriage.Api.Contracts;

public sealed record IngestTicketRequest(string Subject, string Body);
