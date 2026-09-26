namespace SupportTicketTriage.Domain;

/// Closed for the same reason as <see cref="TicketCategory"/>: a priority the
/// system does not recognise cannot be acted on, so it is a parse failure
/// rather than a value to store.
public enum TicketPriority
{
    Low,
    Normal,
    High,
}

public static class TicketPriorities
{
    public static IReadOnlyList<string> Labels { get; } = ["Low", "Normal", "High"];

    public static bool TryFromLabel(string? label, out TicketPriority priority) =>
        Enum.TryParse(label?.Trim(), ignoreCase: true, out priority) && Enum.IsDefined(priority);
}
