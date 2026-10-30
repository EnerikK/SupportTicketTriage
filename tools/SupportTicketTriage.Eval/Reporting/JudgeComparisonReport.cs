using System.Globalization;
using System.Text;
using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Pricing;

namespace SupportTicketTriage.Eval.Reporting;

public sealed record JudgeConfigurationResult(
    string Model,
    JudgeReasoningEffort Effort,
    JudgeAgreement Agreement,
    int Grounded,
    int Ungrounded,
    int Unjudged,
    long InputTokens,
    long OutputTokens,
    decimal? Cost);

/// The comparison table, computed from committed artifacts.
///
/// Every figure here is derived from the verdict files and the label file in
/// the repository, so a reader can regenerate it rather than trust a number
/// somebody typed. That is the point of the command: the table was computed
/// in a shell first, and a shell is not an artifact.
///
/// The first configuration passed is the baseline every other is tested
/// against, matching the decision rule in eval/judge/DECISION-RULE.md.
public sealed record JudgeComparisonReport(
    string LabelPath,
    string LabelSha256,
    string LabelCommit,
    int LabelCount,
    int VerdictCount,
    IReadOnlyList<string> JudgedButUnlabelled,
    string RubricVersion,
    IReadOnlyList<JudgeConfigurationResult> Configurations,
    IReadOnlyList<(string Config, McNemarResult Result)> AgainstBaseline,
    string Currency)
{
    public string ToMarkdown()
    {
        var md = new StringBuilder();

        md.AppendLine("# Judge configuration comparison");
        md.AppendLine();
        md.AppendLine(
            $"Rubric `{RubricVersion}`. Labels: `{LabelPath}`, sha256 `{LabelSha256[..12]}`, "
            + $"committed in `{LabelCommit[..8]}`.");
        md.AppendLine();
        md.AppendLine($"{VerdictCount} pairs judged, {LabelCount} labelled, so {LabelCount} compared.");

        if (JudgedButUnlabelled.Count > 0)
        {
            md.AppendLine();
            md.AppendLine(
                "Judged but deliberately unlabelled, and therefore excluded: "
                + $"{string.Join(", ", JudgedButUnlabelled.Select(id => $"`{id}`"))}. An unlabelled "
                + "pair is dropped rather than guessed; counting it either way would put invented "
                + "data into the figure this table exists to produce.");
        }

        md.AppendLine();
        md.AppendLine("| configuration | agreement | 95% CI | kappa | grounded/ungrounded | output tokens | cost |");
        md.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var c in Configurations)
        {
            // Every number formatted invariantly and explicitly.
            //
            // ADR-017's rule, and the reason it exists: this report was
            // written once with the interpolation handler's default culture
            // and produced "54,3%" and a currency placeholder on a Greek
            // machine while CI produced "54.3%". A machine-comparable
            // artifact that changes with the developer's locale is not
            // comparable. The concatenation here is what forces the issue -
            // StringBuilder's culture-aware overload only binds to a single
            // interpolated literal - so the values are made into strings
            // first rather than trusting the call to carry a provider.
            var agreement = Percent(c.Agreement.RawAgreement, 1);
            var lower = Percent(c.Agreement.AgreementLower, 0);
            var upper = Percent(c.Agreement.AgreementUpper, 0);
            var kappa = c.Agreement.Kappa.ToString("F2", CultureInfo.InvariantCulture);
            var tokens = c.OutputTokens.ToString("N0", CultureInfo.InvariantCulture);
            var cost = c.Cost is null
                ? "not priced"
                : $"{Currency} {c.Cost.Value.ToString("F4", CultureInfo.InvariantCulture)}";

            md.AppendLine(
                $"| `{c.Model}` / {c.Effort} | {agreement}% | {lower}-{upper}% | "
                + $"{kappa} | {c.Grounded}/{c.Ungrounded} | {tokens} | {cost} |");
        }

        md.AppendLine();
        md.AppendLine("Agreement is with the hand labels, which are one person's reading of the rubric "
            + "rather than ground truth. A configuration agreeing less does not thereby judge worse; "
            + "it judges differently from the labeller.");

        md.AppendLine();
        md.AppendLine(
            $"## Against the baseline (`{Configurations[0].Model}` / {Configurations[0].Effort})");
        md.AppendLine();
        md.AppendLine("| configuration | baseline only correct | this only correct | p | |");
        md.AppendLine("|---|---|---|---|---|");

        foreach (var (config, result) in AgainstBaseline)
        {
            var p = result.PValue.ToString("F4", CultureInfo.InvariantCulture);

            md.AppendLine(
                $"| {config} | {result.OnlyFirstCorrect} | {result.OnlySecondCorrect} | "
                + $"{p} | {(result.Significant ? "**significant**" : "not significant")} |");
        }

        md.AppendLine();
        md.AppendLine("McNemar's exact test on discordant pairs, two-sided, alpha 0.05. Only items where "
            + "exactly one configuration matched the label carry information; items both matched, or "
            + "both missed, are excluded by construction.");

        return md.ToString();
    }

    public static JudgeComparisonReport Build(
        LabelProvenance provenance,
        IReadOnlyDictionary<string, bool> labels,
        IReadOnlyList<JudgeVerdictFile> verdictFiles,
        ModelPricing? pricing)
    {
        var configurations = new List<JudgeConfigurationResult>();

        foreach (var file in verdictFiles)
        {
            var cases = file.Verdicts
                .Select(v => new GroundednessCase(v.QueryId, v.Grounded, v.UnsupportedClaim,
                    v.InputTokens, v.OutputTokens, v.AdditionalCounts))
                .ToList();

            var score = GroundednessMetrics.Summarise(cases);
            var price = pricing?.For(file.Model);
            var input = file.Verdicts.Sum(v => v.InputTokens ?? 0);
            var output = file.Verdicts.Sum(v => v.OutputTokens ?? 0);

            configurations.Add(new JudgeConfigurationResult(
                file.Model,
                file.Effort,
                GroundednessMetrics.Compare(cases, labels),
                score.Grounded,
                score.Ungrounded,
                score.Unjudged,
                input,
                output,
                // Null rather than zero when the model is absent from the
                // pricing file: an unpriced configuration must not appear to
                // be the cheapest one.
                price is null
                    ? null
                    : (input * price.InputPerMillionTokens / 1_000_000m)
                      + (output * (price.OutputPerMillionTokens ?? 0m) / 1_000_000m)));
        }

        var baseline = ToMap(verdictFiles[0]);

        var against = verdictFiles
            .Skip(1)
            .Select(f => (
                Config: $"`{f.Model}` / {f.Effort}",
                Result: McNemar.Compare(labels, baseline, ToMap(f))))
            .ToList();

        var judged = verdictFiles[0].Verdicts.Select(v => v.QueryId).ToList();

        return new JudgeComparisonReport(
            provenance.Path,
            provenance.ContentSha256,
            provenance.LastCommit,
            labels.Count,
            judged.Count,
            judged.Where(id => !labels.ContainsKey(id)).ToList(),
            verdictFiles[0].RubricVersion,
            configurations,
            against,
            pricing?.Currency ?? "USD");
    }

    private static string Percent(double value, int decimals) =>
        (value * 100).ToString($"F{decimals}", CultureInfo.InvariantCulture);

    private static Dictionary<string, bool> ToMap(JudgeVerdictFile file) =>
        file.Verdicts
            .Where(v => v.Grounded is not null)
            .ToDictionary(v => v.QueryId, v => v.Grounded!.Value);
}
