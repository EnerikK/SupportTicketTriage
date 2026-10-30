using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.Eval.Judging;

/// One judged draft.
///
/// <see cref="Grounded"/> is nullable because "the judge could not be read"
/// is a third outcome, and folding it into false would make an unparseable
/// response indistinguishable from a draft the judge actually rejected -
/// which is the same mistake [[ADR-010]] avoids by keeping DraftOutcomeKind
/// four-valued.
public sealed record GroundednessCase(
    string QueryId,
    bool? Grounded,
    string? UnsupportedClaim,
    /// What the call actually used. Reasoning tokens are the evidence that a
    /// configured effort reached the API at all: `RawRepresentationFactory`
    /// hands the provider an options object it is free to ignore, and an
    /// ignored setting looks exactly like a working one from the verdict
    /// alone. `AdditionalCounts` is recorded wholesale rather than reduced to
    /// a hardcoded key, so whatever the provider reports survives a rename.
    long? InputTokens = null,
    long? OutputTokens = null,
    IReadOnlyDictionary<string, long>? AdditionalCounts = null);

/// Scores drafts for groundedness using a second, stronger chat deployment.
///
/// ADR-018: a model scoring its own output exhibits documented
/// self-preference, so the judge must not be the deployment that wrote the
/// draft. The separation does not remove bias - both models share a training
/// lineage - it removes the most obvious objection, and that residual belongs
/// in the README beside the number.
public sealed class GroundednessJudge(
    IChatClient judge,
    string deployment,
    JudgeReasoningEffort effort = JudgeReasoningEffort.Default)
{
    public string Deployment { get; } = deployment;

    public JudgeReasoningEffort Effort { get; } = effort;

    /// judge-v2 returns every unsupported material claim, severest first.
    ///
    /// v1 returned the single shortest quote, which turned out to be a real
    /// handicap when reading disagreements: the shortest unsupported phrase is
    /// often trivial while the serious problem sits elsewhere in the draft, so
    /// several cases were categorised by the wrong failure. The array is kept
    /// whole and joined only for display.
    private sealed record Verdict(
        [property: JsonPropertyName("grounded")] bool Grounded,
        [property: JsonPropertyName("unsupported_claims")] string[]? UnsupportedClaims);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<GroundednessCase> JudgeAsync(
        string queryId,
        string draftText,
        string redactedTicket,
        IReadOnlyList<EvidenceSource> citedSources,
        CancellationToken ct = default)
    {
        // A draft that cited nothing is ungrounded by definition rather than
        // by judgement: every factual claim in it is unsupported, because
        // there is no source to support any of them. Spending a judge call to
        // be told that would be paying for arithmetic.
        if (citedSources.Count == 0)
        {
            return new GroundednessCase(queryId, Grounded: false, "The draft cited no sources.");
        }

        var delimiter = GroundednessPrompt.NewDelimiter();

        try
        {
            var response = await judge.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, GroundednessPrompt.System(delimiter)),
                    new ChatMessage(
                        ChatRole.User,
                        GroundednessPrompt.User(delimiter, draftText, redactedTicket, citedSources)),
                ],
                JudgeChatOptions.For(Effort),
                ct);

            var verdict = Parse(response.Text);
            var usage = response.Usage;
            var extra = usage?.AdditionalCounts?.ToDictionary(c => c.Key, c => c.Value);

            return verdict is null
                ? new GroundednessCase(
                    queryId, Grounded: null, "The judge response could not be read.",
                    usage?.InputTokenCount, usage?.OutputTokenCount, extra)
                : new GroundednessCase(
                    queryId, verdict.Grounded, Claims(verdict.UnsupportedClaims),
                    usage?.InputTokenCount, usage?.OutputTokenCount, extra);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unjudged, not ungrounded. A judge outage must not quietly
            // depress the score - the report counts these separately and
            // excludes them from the denominator.
            return new GroundednessCase(queryId, Grounded: null, $"The judge call failed: {ex.GetType().Name}.");
        }
    }

    /// Tolerates a fenced code block around the object, which is the one
    /// deviation chat models produce often enough to be worth absorbing
    /// rather than counting as a failure.
    private static Verdict? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Verdict>(text[start..(end + 1)], Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// Joined for storage because the verdict file and every report treat
    /// this as one human-readable field; the order carries the judge's own
    /// severity ranking and is preserved.
    private static string? Claims(string[]? claims)
    {
        var kept = (claims ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();

        return kept.Count == 0 ? null : string.Join(" | ", kept);
    }
}
