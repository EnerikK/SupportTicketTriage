using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

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
    string? UnsupportedClaim);

/// Scores drafts for groundedness using a second, stronger chat deployment.
///
/// ADR-018: a model scoring its own output exhibits documented
/// self-preference, so the judge must not be the deployment that wrote the
/// draft. The separation does not remove bias - both models share a training
/// lineage - it removes the most obvious objection, and that residual belongs
/// in the README beside the number.
public sealed class GroundednessJudge(IChatClient judge, string deployment)
{
    public string Deployment { get; } = deployment;

    private sealed record Verdict(
        [property: JsonPropertyName("grounded")] bool Grounded,
        [property: JsonPropertyName("unsupported_claim")] string? UnsupportedClaim);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<GroundednessCase> JudgeAsync(
        string queryId,
        string draftText,
        IReadOnlyList<GroundednessPrompt.Source> citedSources,
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
                    new ChatMessage(ChatRole.User, GroundednessPrompt.User(delimiter, draftText, citedSources)),
                ],
                cancellationToken: ct);

            var verdict = Parse(response.Text);

            return verdict is null
                ? new GroundednessCase(queryId, Grounded: null, "The judge response could not be read.")
                : new GroundednessCase(queryId, verdict.Grounded, Blank(verdict.UnsupportedClaim));
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

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
