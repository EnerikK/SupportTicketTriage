using System.Text.Json;
using SupportTicketTriage.Application.Redaction;
using SupportTicketTriage.Eval.Dataset;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.Eval.Judging;

public sealed record DevCheckExpectations(
    string RubricVersion,
    IReadOnlyDictionary<string, string> Excluded,
    IReadOnlyDictionary<string, bool> Expected,
    IReadOnlyDictionary<string, string> Why);

public sealed record DevCheckOutcome(string QueryId, bool Expected, bool? Actual, string? Claims, string Why)
{
    public bool Matched => Actual == Expected;
}

/// A behavioural check that judge-v2 does what the rules say, run against the
/// 46 development pairs before any held-out run.
///
/// **Never a reported number.** These pairs produced the rules; they cannot
/// also test them. Their expected verdicts were derived case by case from the
/// rubric text, not carried over from the categories in DISAGREEMENTS.md,
/// because those categories were assigned by reading what judge-v1 flagged
/// and inherit its blind spot.
///
/// The protocol for a mismatch is fixed in DECISION-RULE-V2.md and is the
/// point of running this at all: each one is either rubric ambiguity (the
/// wording admits the judge's reading - fix it and log the change) or judge
/// error (the wording is clear - record it, change nothing). The wording is
/// not iterated toward a perfect score, because a rubric fitted to one judge
/// on one sample would make the held-out run measure nothing.
public static class DevCheck
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<DevCheckExpectations> LoadAsync(string path, CancellationToken ct = default)
    {
        var file = JsonSerializer.Deserialize<DevCheckExpectations>(
            await File.ReadAllTextAsync(path, ct), Json)
            ?? throw new InvalidOperationException($"'{path}' is not a dev-check expectations file.");

        if (file.RubricVersion != GroundednessPrompt.Version)
        {
            throw new InvalidOperationException(
                $"'{path}' expects rubric {file.RubricVersion}, but this harness runs "
                + $"{GroundednessPrompt.Version}. Re-derive the expectations: a verdict expected "
                + "under one rubric says nothing about another.");
        }

        return file;
    }

    /// Rebuilds the full evidence for a pair exported before judge-v2.
    ///
    /// The v1 pair file carries each precedent's subject and resolution but
    /// not its body, and no ticket text at all - the two things v2 added. Both
    /// are recovered from the committed dataset by matching a source's
    /// resolution text back to the corpus ticket that carries it, which works
    /// because resolutions are unique and is the only join available: the ids
    /// in the pair file are database GUIDs from a run whose database has since
    /// been reset.
    public static (string Ticket, List<EvidenceSource> Sources)? Rehydrate(
        JsonElement pair,
        EvalDataset dataset)
    {
        var redactor = new PiiRedactor();
        var queryId = pair.GetProperty("queryId").GetString()!;
        var query = dataset.Tickets.FirstOrDefault(t => t.Id == queryId);

        if (query is null)
        {
            return null;
        }

        var byResolution = dataset.Tickets
            .Where(t => !string.IsNullOrWhiteSpace(t.Resolution))
            .GroupBy(t => t.Resolution!.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        var sources = new List<EvidenceSource>();

        foreach (var cited in pair.GetProperty("citedSources").EnumerateArray())
        {
            var resolution = cited.GetProperty("resolution").GetString()!.Trim();

            if (!byResolution.TryGetValue(resolution, out var source))
            {
                return null;
            }

            sources.Add(new EvidenceSource(
                cited.GetProperty("ticketId").GetGuid(),
                redactor.Redact(source.Subject),
                redactor.Redact(source.Body),
                redactor.Redact(source.Resolution!)));
        }

        return (redactor.Redact($"{query.Subject}\n\n{query.Body}"), sources);
    }

    public static string Report(IReadOnlyList<DevCheckOutcome> outcomes)
    {
        var matched = outcomes.Count(o => o.Matched);
        var lines = new List<string>
        {
            $"Dev check against rubric {GroundednessPrompt.Version}: "
            + $"{matched}/{outcomes.Count} matched the expected verdict.",
            string.Empty,
            "This is a behavioural check on development data and is never a reported",
            "number. Classify each mismatch below as rubric ambiguity (fix the wording,",
            "log the change) or judge error (record it, change nothing). Do not iterate",
            "the wording toward a perfect score.",
            string.Empty,
        };

        foreach (var o in outcomes.Where(o => !o.Matched))
        {
            lines.Add($"MISMATCH {o.QueryId}");
            lines.Add($"  expected {o.Expected}, judge said {o.Actual?.ToString() ?? "unjudged"}");
            lines.Add($"  rule     {o.Why}");
            lines.Add($"  judge    {o.Claims ?? "(no claims returned)"}");
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
