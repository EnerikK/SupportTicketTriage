using System.Globalization;
using System.Text;
using System.Text.Json;
using SupportTicketTriage.Eval.Embedding;
using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.Eval.Reporting;

/// One evaluation run.
///
/// `Classification` and `Citations` are null when the run had no chat
/// deployment — the harness still scores retrieval offline against the
/// lexical stand-in, and a null section says "not measured" rather than
/// reporting a zero that would read as a failure.
public sealed record GateCalibration(
    string Gate,
    string ScoreName,
    string SuccessName,
    double? Separation,
    int AbsentScore,
    IReadOnlyList<FloorOutcome> Floors,
    /// Scores of observations excluded from the table because success is
    /// undefined for them - a malformed ticket has no correct answer, so
    /// "retrieval succeeded" cannot be evaluated. They are not evidence of
    /// where the floor belongs, but they are exactly what the gate exists to
    /// catch, so a floor above all of them is doing its job.
    IReadOnlyList<double> UndefinedOutcomeScores);

public sealed record EvaluationReport(
    string DatasetVersion,
    string EmbeddingModel,
    bool EmbeddingModelIsReal,
    string? ChatModel,
    DateTimeOffset GeneratedAt,
    int CorpusCount,
    int QueryCount,
    int ScoredQueryCount,
    int UnscoredQueryCount,
    IReadOnlyDictionary<int, double> RecallAtK,
    ClassificationScore? Classification,
    CitationScore? Citations,
    IReadOnlyList<GateCalibration> Calibration,
    IReadOnlyDictionary<string, int> TraitCoverage)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task WriteJsonAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(this, Json), ct);
    }

    public async Task WriteMarkdownAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ToMarkdown(), ct);
    }

    public string ToMarkdown()
    {
        var md = new StringBuilder();

        md.AppendLine("# Evaluation");
        md.AppendLine();

        if (!EmbeddingModelIsReal)
        {
            md.AppendLine(
                $"> **These numbers are not a baseline.** They were produced with `{EmbeddingModel}`, a "
                + "deterministic lexical stand-in, not a real embedding deployment. It matches on shared "
                + "wording and captures no semantics, so paraphrases score as unrelated. The figures below "
                + "show the harness works; they say nothing about retrieval quality.");
            md.AppendLine();
        }

        md.AppendLine($"- Dataset version: `{DatasetVersion}`");
        md.AppendLine($"- Embedding model: `{EmbeddingModel}`");
        md.AppendLine($"- Chat model: {(ChatModel is null ? "_none — classification and generation not scored_" : $"`{ChatModel}`")}");
        md.AppendLine($"- Generated: {GeneratedAt:u}");
        md.AppendLine($"- Corpus tickets: {CorpusCount}");
        md.AppendLine($"- Queries: {QueryCount} ({ScoredQueryCount} scored, {UnscoredQueryCount} unlabelled)");
        md.AppendLine();

        AppendRetrieval(md);
        AppendClassification(md);
        AppendCitations(md);
        AppendCalibration(md);
        AppendTraits(md);

        return md.ToString();
    }

    private void AppendRetrieval(StringBuilder md)
    {
        md.AppendLine("## Recall@k");
        md.AppendLine();
        md.AppendLine("| k | mean recall |");
        md.AppendLine("|---|---|");
        foreach (var (k, recall) in RecallAtK.OrderBy(kv => kv.Key))
        {
            // Formatted invariantly, and that is not a detail. This report is an
            // artifact compared across machines and pasted into the README; on a
            // locale whose decimal separator is a comma, plain interpolation
            // renders 75.9% as "75,9%" and the same run reads differently
            // depending on who ran it. The JSON beside it escapes this only
            // because System.Text.Json is always invariant.
            //
            // Not ":P1" even invariantly: InvariantCulture renders a percentage
            // as "75.9 %", with a space, and the README quotes "75.9%".
            // Scaling and appending the sign keeps the artifact identical
            // everywhere and identical to how the number is quoted elsewhere.
            md.AppendLine(CultureInfo.InvariantCulture, $"| {k} | {recall * 100:F1}% |");
        }

        md.AppendLine();
        md.AppendLine(
            "Relevance labels were written by hand alongside the dataset, so recall partly measures our own "
            + "labelling rather than retrieval alone. Hard negatives and near-duplicates reduce that "
            + "circularity but do not remove it.");
        md.AppendLine();
    }

    private void AppendClassification(StringBuilder md)
    {
        md.AppendLine("## Classification");
        md.AppendLine();

        if (Classification is null)
        {
            md.AppendLine("_Not scored: this run had no chat deployment._");
            md.AppendLine();
            return;
        }

        md.AppendLine(CultureInfo.InvariantCulture, $"- Accuracy: **{Classification.Accuracy * 100:F1}%**");
        md.AppendLine(CultureInfo.InvariantCulture, $"- Macro-F1: **{Classification.MacroF1 * 100:F1}%**");
        md.AppendLine(CultureInfo.InvariantCulture,
            $"- Scored {Classification.Total} tickets, of which {Classification.Unclassified} could not be classified");
        md.AppendLine();

        md.AppendLine("| category | precision | recall | F1 | support |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (var label in Classification.PerLabel)
        {
            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {label.Label} | {label.Precision * 100:F1}% | {label.Recall * 100:F1}% | {label.F1 * 100:F1}% | {label.Support} |");
        }

        md.AppendLine();
        md.AppendLine(
            "An unclassified ticket counts as wrong rather than being excluded: the dataset carries the "
            + "label, so a response that could not be parsed got it wrong. Macro-F1 is the unweighted mean "
            + "over categories with support, so a category the classifier never predicts drags it down "
            + "instead of hiding behind the volume of the ones it does.");
        md.AppendLine();
        md.AppendLine(
            "**The dataset is balanced at 50 tickets per category, which production ticket volume will not "
            + "be.** A balanced set makes macro-F1 and accuracy converge; on a real skewed distribution they "
            + "would not, and macro-F1 would be the more honest of the two.");
        md.AppendLine();
    }

    private void AppendCitations(StringBuilder md)
    {
        md.AppendLine("## Citation validity");
        md.AppendLine();

        if (Citations is null)
        {
            md.AppendLine("_Not scored: this run had no chat deployment._");
            md.AppendLine();
            return;
        }

        md.AppendLine(CultureInfo.InvariantCulture,
            $"- Validity rate: **{Citations.ValidityRate * 100:F1}%** over {Citations.Attempted} generated drafts");
        md.AppendLine(CultureInfo.InvariantCulture,
            $"- {Citations.Valid} fully valid, {Citations.InvalidCitations} citing outside their retrieval set");
        md.AppendLine(CultureInfo.InvariantCulture,
            $"- {Citations.NotEligible} refused by the routing gates, {Citations.Failed} generation failures");
        md.AppendLine(CultureInfo.InvariantCulture,
            $"- {Citations.TotalCitations} citations in total, {Citations.TotalInvalidCitations} of them invalid");
        md.AppendLine();
        md.AppendLine(
            "The denominator is drafts the model actually produced. Tickets the gates refused are excluded, "
            + "because including them would make this number improve every time the gates got stricter — "
            + "measuring the gates rather than the model. Generation failures are excluded for the same "
            + "reason and counted separately: there were no citations to validate.");
        md.AppendLine();
    }


    /// The table a threshold decision actually needs.
    ///
    /// Not "accuracy per bucket" but "if the floor were here, what would it
    /// keep and what would it throw away" — because pass rate alone always
    /// improves as the floor rises, and a gate chosen on pass rate alone ends
    /// up refusing everything.
    private void AppendCalibration(StringBuilder md)
    {
        if (Calibration.Count == 0)
        {
            return;
        }

        md.AppendLine("## Gate calibration");
        md.AppendLine();
        md.AppendLine(
            "These tables are the evidence for choosing the routing floors. Each row is a candidate "
            + "floor; floors are inclusive, matching the gate itself.");
        md.AppendLine();

        foreach (var gate in Calibration)
        {
            md.AppendLine($"### Gate {gate.Gate}");
            md.AppendLine();
            md.AppendLine($"Score: {gate.ScoreName}. Success: {gate.SuccessName}.");
            md.AppendLine();

            // The number that decides whether this gate can work at all.
            md.AppendLine(gate.Separation is null
                ? "**Separation: not computable** — every observation went the same way."
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "**Separation: {0:F3}** — the mean score of the cases that succeeded minus the mean "
                    + "of those that did not. Near zero means the score carries no information about the "
                    + "outcome and no floor will help.",
                    gate.Separation));
            md.AppendLine();

            if (gate.AbsentScore > 0)
            {
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"{gate.AbsentScore} observation(s) had no score at all. Those fail every floor and are excluded from the table - they say nothing about where the floor belongs.");
                md.AppendLine();
            }

            if (gate.UndefinedOutcomeScores.Count > 0)
            {
                var scores = string.Join(", ", gate.UndefinedOutcomeScores.Select(
                    v => v.ToString("F3", CultureInfo.InvariantCulture)));
                var highest = gate.UndefinedOutcomeScores[^1];

                md.AppendLine(CultureInfo.InvariantCulture,
                    $"{gate.UndefinedOutcomeScores.Count} observation(s) have no defined outcome - a query with no correct answer cannot succeed or fail at retrieval - so they are excluded from the table. Their scores were: {scores}. These are precisely what this gate exists to refuse, so **a floor above {highest:F3} refuses all of them**.");
                md.AppendLine();
            }

            md.AppendLine("| floor | passes | success among passed | gated | gated that would have worked | % gated |");
            md.AppendLine("|---|---|---|---|---|---|");

            foreach (var row in gate.Floors)
            {
                md.AppendLine(CultureInfo.InvariantCulture,
                    $"| {row.Floor:F2} | {row.Passed} | {Rate(row.PassRate)} | {row.Gated} | {row.GatedButWouldHaveSucceeded} | {row.GatedShare * 100:F0}% |");
            }

            md.AppendLine();
        }
    }

    private static string Rate(double? rate) =>
        rate is null ? "—" : string.Format(CultureInfo.InvariantCulture, "{0:F1}%", rate * 100);
    private void AppendTraits(StringBuilder md)
    {
        if (TraitCoverage.Count == 0)
        {
            return;
        }

        md.AppendLine("## Dataset composition");
        md.AppendLine();
        md.AppendLine("| trait | tickets |");
        md.AppendLine("|---|---|");
        foreach (var (trait, count) in TraitCoverage.OrderBy(kv => kv.Key))
        {
            md.AppendLine($"| {trait} | {count} |");
        }

        md.AppendLine();
    }

    /// A baseline is what CI compares against, so it must never be written from
    /// a stand-in embedder. Refusing here is cheaper than discovering later
    /// that the gate has been guarding a meaningless number.
    public void EnsureCanBeBaseline()
    {
        if (!EmbeddingModelIsReal)
        {
            throw new InvalidOperationException(
                $"Refusing to write a baseline produced with '{EmbeddingModel}'. "
                + "A baseline requires a real embedding deployment.");
        }
    }

    public static bool IsRealEmbeddingModel(string model) => model != LexicalEmbeddingGenerator.ModelName;
}
