using System.Globalization;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.Eval.Baseline;

public enum ComparisonOutcome
{
    /// Within tolerance, or moved in the direction nobody minds.
    Passed,

    /// Moved further than the tolerance allows, in the direction that matters.
    Regressed,

    /// The baseline gates this metric and the run did not produce it. A failure
    /// rather than a skip: a gate that silently covers less than it claims is
    /// the exact thing this phase exists to prevent.
    NotMeasured,
}

public sealed record MetricComparison(
    string Metric,
    double? Baseline,
    double? Current,
    double Tolerance,
    ComparisonOutcome Outcome)
{
    /// Positive means worse, for every metric. Cost rising and recall falling
    /// both read as a positive delta, so the table can be scanned in one
    /// direction rather than requiring the reader to remember which way each
    /// metric points.
    public double? Regression =>
        Baseline is null || Current is null ? null : Baseline - Current;
}

/// What a run looks like against its baseline.
///
/// **Provenance mismatches are not regressions**, and the distinction is the
/// whole reason the provenance is recorded. If the dataset, a prompt, the
/// redaction rules or the routing floors have moved, the two runs are not
/// measuring the same thing - so the honest answer is "this baseline no longer
/// applies", not "the system got worse". The gate still fails, because the
/// specification says a baseline is never silently regenerated to make CI
/// green; it just fails saying the right thing.
public sealed record BaselineComparison(
    IReadOnlyList<string> ProvenanceMismatches,
    IReadOnlyList<MetricComparison> Metrics)
{
    public bool Comparable => ProvenanceMismatches.Count == 0;

    public bool Passed =>
        Comparable && Metrics.All(m => m.Outcome == ComparisonOutcome.Passed);

    public static BaselineComparison Compare(EvaluationBaseline baseline, EvaluationReport run)
    {
        var tolerances = baseline.Tolerances;

        return new BaselineComparison(
            MismatchedProvenance(baseline, run),
            [
                .. CompareRecall(baseline, run, tolerances.RecallMaxDrop),
                Drop("classification accuracy",
                    baseline.Metrics.ClassificationAccuracy,
                    run.Classification?.Accuracy,
                    tolerances.ClassificationAccuracyMaxDrop),
                Drop("classification macro-F1",
                    baseline.Metrics.ClassificationMacroF1,
                    run.Classification?.MacroF1,
                    tolerances.ClassificationMacroF1MaxDrop),
                Drop("citation validity",
                    baseline.Metrics.CitationValidityRate,
                    run.Citations?.ValidityRate,
                    tolerances.CitationValidityMaxDrop),
                RelativeIncrease("cost total",
                    baseline.Metrics.CostTotal,
                    run.Cost?.Total,
                    tolerances.CostMaxRelativeIncrease),
            ]);
    }

    /// Anything that makes the two runs incomparable.
    ///
    /// Each of these silently changes what the metrics mean rather than how
    /// good they are. A prompt edit that lifts accuracy is an improvement; a
    /// redaction bump makes recall figures non-comparable rather than merely
    /// different ([[ADR-005]]); moving a routing floor changes `NotEligible`,
    /// which is citation validity's denominator, so a retune would otherwise
    /// surface as a citation regression.
    private static List<string> MismatchedProvenance(EvaluationBaseline baseline, EvaluationReport run)
    {
        var p = baseline.Provenance;
        var mismatches = new List<string>();

        void Check(string what, string? expected, string? actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"{what}: baseline '{expected ?? "none"}', run '{actual ?? "none"}'");
            }
        }

        void CheckNumber(string what, double expected, double actual)
        {
            if (Math.Abs(expected - actual) > 1e-9)
            {
                mismatches.Add(string.Format(
                    CultureInfo.InvariantCulture, "{0}: baseline {1}, run {2}", what, expected, actual));
            }
        }

        Check("dataset version", p.DatasetVersion, run.DatasetVersion);
        Check("embedding model", p.EmbeddingModel, run.EmbeddingModel);
        Check("chat model", p.ChatModel, run.ChatModel);
        Check("classification prompt", p.ClassificationPromptVersion, run.ClassificationPromptVersion);
        Check("generation prompt", p.GenerationPromptVersion, run.GenerationPromptVersion);
        Check("redaction rules", p.RedactionVersion, run.RedactionVersion);
        CheckNumber("Gate A floor", p.MinimumClassificationScore, run.MinimumClassificationScore);
        CheckNumber("Gate B floor", p.MinimumSimilarity, run.MinimumSimilarity);

        return mismatches;
    }

    private static IEnumerable<MetricComparison> CompareRecall(
        EvaluationBaseline baseline,
        EvaluationReport run,
        double tolerance) =>
        baseline.Metrics.RecallAtK
            .OrderBy(kv => kv.Key)
            .Select(kv => Drop(
                $"recall@{kv.Key}",
                kv.Value,
                run.RecallAtK.TryGetValue(kv.Key, out var actual) ? actual : null,
                tolerance));

    /// Only a fall counts. A gate that fires when a number improves is one
    /// people learn to route around, and then it protects nothing.
    private static MetricComparison Drop(
        string metric, double? baseline, double? current, double tolerance)
    {
        if (baseline is null)
        {
            // The baseline does not gate this metric, so the run is free to
            // report it or not.
            return new MetricComparison(metric, null, current, tolerance, ComparisonOutcome.Passed);
        }

        if (current is null)
        {
            return new MetricComparison(metric, baseline, null, tolerance, ComparisonOutcome.NotMeasured);
        }

        var outcome = baseline.Value - current.Value > tolerance
            ? ComparisonOutcome.Regressed
            : ComparisonOutcome.Passed;

        return new MetricComparison(metric, baseline, current, tolerance, outcome);
    }

    /// The mirror of Drop, for the one metric where up is the bad direction.
    private static MetricComparison RelativeIncrease(
        string metric, decimal? baseline, decimal? current, double tolerance)
    {
        if (baseline is null)
        {
            return new MetricComparison(metric, null, (double?)current, tolerance, ComparisonOutcome.Passed);
        }

        if (current is null)
        {
            return new MetricComparison(
                metric, (double)baseline.Value, null, tolerance, ComparisonOutcome.NotMeasured);
        }

        // A baseline cost of zero cannot be exceeded by a ratio, so fall back
        // to "any increase is a regression" rather than dividing by it.
        var regressed = baseline.Value == 0m
            ? current.Value > 0m
            : (double)((current.Value - baseline.Value) / baseline.Value) > tolerance;

        return new MetricComparison(
            metric,
            (double)baseline.Value,
            (double)current.Value,
            tolerance,
            regressed ? ComparisonOutcome.Regressed : ComparisonOutcome.Passed);
    }

    /// A report a human reads when CI goes red, formatted invariantly like
    /// every other artifact here.
    public string ToMarkdown()
    {
        var md = new System.Text.StringBuilder();

        md.AppendLine("## Regression gate");
        md.AppendLine();

        if (!Comparable)
        {
            md.AppendLine(
                "**This baseline no longer applies.** The run and the baseline differ in what they "
                + "were produced from, so their numbers are not measuring the same thing - this is "
                + "not a regression, and regenerating the baseline is the fix once the change is "
                + "deliberate.");
            md.AppendLine();

            foreach (var mismatch in ProvenanceMismatches)
            {
                md.AppendLine($"- {mismatch}");
            }

            md.AppendLine();
            return md.ToString();
        }

        md.AppendLine("| metric | baseline | run | change | tolerance | |");
        md.AppendLine("|---|---|---|---|---|---|");

        foreach (var m in Metrics)
        {
            var mark = m.Outcome switch
            {
                ComparisonOutcome.Regressed => "**REGRESSED**",
                ComparisonOutcome.NotMeasured => "**NOT MEASURED**",
                _ => "ok",
            };

            md.AppendLine(CultureInfo.InvariantCulture,
                $"| {m.Metric} | {Number(m.Baseline)} | {Number(m.Current)} | {Signed(m.Regression)} | {Number(m.Tolerance)} | {mark} |");
        }

        md.AppendLine();
        md.AppendLine(Passed
            ? "No regression beyond tolerance."
            : "**The gate failed.** Change is baseline minus run, so a positive number is the "
              + "direction that matters for every row - including cost, where the run costing more "
              + "shows as a negative baseline-minus-run and is judged on relative increase instead.");
        md.AppendLine();

        return md.ToString();
    }

    private static string Number(double? value) =>
        value is null ? "-" : value.Value.ToString("F4", CultureInfo.InvariantCulture);

    private static string Signed(double? value) =>
        value is null ? "-" : value.Value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture);
}
