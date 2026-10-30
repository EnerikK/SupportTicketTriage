using System.Text;

namespace SupportTicketTriage.Infrastructure.Ai;

/// One retrieved precedent, already redacted.
///
/// Carries the subject and the body as well as the resolution because a
/// precedent is a customer ticket plus an agent's answer, and the three have
/// different evidentiary weight. The writer and the judge are shown all
/// three; only the resolution establishes facts.
public readonly record struct EvidenceSource(
    Guid TicketId,
    string Subject,
    string RedactedText,
    string RedactedResolution);

/// Renders the evidence blocks for both the generation prompt and the judge.
///
/// **Shared because an asymmetry here is invisible and expensive.** The judge
/// prompt used to omit each precedent's subject and body, so the judge scored
/// drafts against strictly less evidence than the writer was given, and any
/// claim resting on a precedent's own words was unsupported by construction.
/// Three of twenty disagreements in the first comparison were that bug rather
/// than a judgement. One function means the two cannot drift apart again, and
/// a test asserts they render identically.
///
/// The labels on each block are load-bearing rather than decoration: they are
/// what the rubric's evidentiary-status rule refers to. ACCOUNT is what a
/// customer said and never establishes a fact; RESOLUTION is what an agent
/// did and does.
public static class EvidenceBlocks
{
    public const string AccountLabel = "ACCOUNT (customer's own words - not evidence for any fact)";
    public const string ResolutionLabel = "RESOLUTION (agent - this establishes facts)";

    /// The ticket under consideration, fenced and labelled as an account.
    public static void AppendIncomingTicket(StringBuilder builder, string delimiter, string redactedTicket)
    {
        builder.AppendLine(delimiter);
        builder.AppendLine("INCOMING TICKET");
        builder.AppendLine(AccountLabel);
        builder.AppendLine(redactedTicket);
        builder.AppendLine(delimiter);
    }

    /// Each precedent in its own fenced block, tagged with the id a draft must
    /// cite, so one source cannot be confused for another or for the ticket.
    public static void AppendPrecedents(
        StringBuilder builder,
        string delimiter,
        IReadOnlyList<EvidenceSource> sources)
    {
        foreach (var source in sources)
        {
            builder.AppendLine();
            builder.AppendLine(delimiter);
            builder.AppendLine($"PRECEDENT id={source.TicketId}");
            builder.AppendLine($"SUBJECT ({AccountLabel}): {source.Subject}");
            builder.AppendLine(AccountLabel);
            builder.AppendLine(source.RedactedText);
            builder.AppendLine(ResolutionLabel);
            builder.AppendLine(source.RedactedResolution);
            builder.AppendLine(delimiter);
        }
    }

    public static string NewDelimiter() => $"---block-{Guid.NewGuid():N}---";
}
