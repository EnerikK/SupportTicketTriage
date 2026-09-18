using System.Text.RegularExpressions;

namespace SupportTicketTriage.Application.Redaction;

/// Removes direct identifiers from ticket text before it crosses the AI boundary.
public sealed partial class PiiRedactor
{
    // Stamped alongside every stored embedding. Changing any rule below means
    // bumping this, because existing vectors were produced under the old rules.
    public const string Version = "v1";

    public string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Order matters. Emails are matched first so their digits cannot be
        // consumed by a number rule, and cards before phones because a card is
        // also a valid phone-length run of digits.
        var redacted = EmailPattern().Replace(text, "[EMAIL]");
        redacted = IpV4Pattern().Replace(redacted, "[IP]");
        redacted = CardPattern().Replace(redacted, "[CARD]");
        redacted = PhonePattern().Replace(redacted, "[PHONE]");

        return redacted;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.ExplicitCapture)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.ExplicitCapture)]
    private static partial Regex IpV4Pattern();

    // 13-19 digits, optionally separated by single spaces or hyphens. No Luhn
    // check: for a PII boundary an over-redacted order number is a cosmetic
    // loss, while an under-redacted card number is a leak.
    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b", RegexOptions.ExplicitCapture)]
    private static partial Regex CardPattern();

    // 7-15 digits with common separators, allowing runs like ") " between
    // groups and an optional opening parenthesis. Dots are deliberately
    // excluded so that dotted-quad IP addresses cannot match here.
    [GeneratedRegex(@"\+?\(?\d(?:[ ()-]{0,2}\d){6,14}\b", RegexOptions.ExplicitCapture)]
    private static partial Regex PhonePattern();
}
