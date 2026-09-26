namespace SupportTicketTriage.Domain;

/// The closed set of categories a ticket may be classified into.
///
/// A closed set rather than free text because the classifier is an external
/// model: an unrecognised value is a failure to route on, not a new category
/// to accept. <see cref="TicketCategories"/> holds the labels, which differ
/// from the member names because several contain characters C# does not allow.
public enum TicketCategory
{
    Billing,
    AccountAndLogin,
    TechnicalFault,
    ShippingAndDelivery,
    ReturnsAndRefunds,
    ProductQuestion,
}

public static class TicketCategories
{
    /// The wire labels. These are the exact strings the evaluation dataset uses
    /// and the exact strings the model is told to choose between, so the two
    /// can be compared without a translation step in between.
    private static readonly (TicketCategory Category, string Label)[] Pairs =
    [
        (TicketCategory.Billing, "Billing"),
        (TicketCategory.AccountAndLogin, "Account & Login"),
        (TicketCategory.TechnicalFault, "Technical Fault"),
        (TicketCategory.ShippingAndDelivery, "Shipping & Delivery"),
        (TicketCategory.ReturnsAndRefunds, "Returns & Refunds"),
        (TicketCategory.ProductQuestion, "Product Question"),
    ];

    public static IReadOnlyList<string> Labels { get; } = Pairs.Select(p => p.Label).ToArray();

    public static string ToLabel(this TicketCategory category) =>
        Pairs.First(p => p.Category == category).Label;

    /// Matches on the label only, case-insensitively. Deliberately does not fall
    /// back to parsing the enum member name: accepting "AccountAndLogin" would
    /// let a second spelling of the same category into the system.
    public static bool TryFromLabel(string? label, out TicketCategory category)
    {
        foreach (var pair in Pairs)
        {
            if (string.Equals(pair.Label, label?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                category = pair.Category;
                return true;
            }
        }

        category = default;
        return false;
    }
}
