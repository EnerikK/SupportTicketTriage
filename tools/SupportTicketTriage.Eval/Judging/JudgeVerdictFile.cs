using System.Text.Json;

namespace SupportTicketTriage.Eval.Judging;

public sealed record JudgeVerdict(
    string QueryId,
    bool? Grounded,
    string? UnsupportedClaim,
    long? InputTokens,
    long? OutputTokens,
    IReadOnlyDictionary<string, long>? AdditionalCounts);

/// What one judge configuration said, and what it spent saying it.
///
/// **Deliberately not written into the hand-labelling file.** That file is the
/// labeller's input and carries `grounded: null` by design; putting a verdict
/// beside the blank would anchor the human to the machine's answer, and
/// [[ADR-029]] records that the whole point of these labels is to validate an
/// LLM judge against something that is not one. An anchored label looks
/// identical to an independent one in the file and destroys the figure it was
/// collected for. One artifact per configuration, keyed by query id, joined to
/// the labels only at comparison time.
///
/// `Effort` and the token counts travel together because the first is a claim
/// and the second is the evidence: `RawRepresentationFactory` hands the
/// provider an options object it is free to ignore, so a configured effort
/// that never reached the API is indistinguishable from a working one by the
/// verdicts alone.
public sealed record JudgeVerdictFile(
    string Model,
    string RubricVersion,
    JudgeReasoningEffort Effort,
    DateTimeOffset RunAt,
    IReadOnlyList<JudgeVerdict> Verdicts)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task WriteAsync(
        string path,
        string model,
        JudgeReasoningEffort effort,
        IReadOnlyList<GroundednessCase> cases,
        CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new JudgeVerdictFile(
            model,
            GroundednessPrompt.Version,
            effort,
            DateTimeOffset.UtcNow,
            cases.Select(c => new JudgeVerdict(
                c.QueryId,
                c.Grounded,
                c.UnsupportedClaim,
                c.InputTokens,
                c.OutputTokens,
                c.AdditionalCounts)).ToList());

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, Json), ct);
    }

    /// Total reasoning tokens across the run, under whatever key the provider
    /// used. Read by matching on the key name rather than a constant, because
    /// the bridge's naming is not a contract this repository controls.
    public long? ReasoningTokens =>
        Verdicts
            .Select(v => v.AdditionalCounts?
                .FirstOrDefault(c => c.Key.Contains("reasoning", StringComparison.OrdinalIgnoreCase))
                .Value)
            .Where(v => v is > 0)
            .Aggregate((long?)null, (total, v) => (total ?? 0) + v!.Value);
}
