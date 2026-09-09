namespace SupportTicketTriage.Domain;

public sealed class Ticket
{
    public Guid Id { get; private init; }
    public string Subject { get; private init; }
    public string Body { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private Ticket(Guid id, string subject, string body, DateTimeOffset createdAt)
    {
        Id = id;
        Subject = subject;
        Body = body;
        CreatedAt = createdAt;
    }

    public static Ticket Create(string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Body is required.", nameof(body));
        }

        return new Ticket(Guid.CreateVersion7(), subject, body, UtcNowToMicrosecondPrecision());
    }

    private static DateTimeOffset UtcNowToMicrosecondPrecision()
    {
        var now = DateTimeOffset.UtcNow;

        // Postgres timestamptz only stores microsecond precision (1000ns), but
        // DateTimeOffset ticks are 100ns - truncate here so the in-memory value
        // matches what a round trip through the database actually returns.
        return now.AddTicks(-(now.Ticks % 10));
    }
}
