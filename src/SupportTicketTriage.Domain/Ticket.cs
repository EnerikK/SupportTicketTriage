namespace SupportTicketTriage.Domain;

public sealed class Ticket
{
    public Guid Id { get; private init; }
    public string Subject { get; private init; }
    public string Body { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    // A ticket is corpus-eligible exactly when it carries an approved
    // resolution. The presence of the resolution is the state; a separate
    // status enum would duplicate it until the review workflow needs one.
    public string? Resolution { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => Resolution is not null;

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

    public void Resolve(string resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            throw new ArgumentException("Resolution is required.", nameof(resolution));
        }

        if (IsResolved)
        {
            throw new InvalidOperationException($"Ticket {Id} is already resolved.");
        }

        Resolution = resolution;
        ResolvedAt = UtcNowToMicrosecondPrecision();
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
