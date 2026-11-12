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
    IReadOnlyList<(string Config, McNemarResult Result)> AgainstBaselineUngroundedOnly,
    string Currency)
{
    /// The false-flag ceiling, in whole percent, from
    /// eval/judge/DECISION-RULE-V2.md.
    ///
    /// A constant rather than an option because ADR-037 freezes it: "the 25%
    /// false-flag ceiling, the ordering, and the no-eligible-configuration
    /// outcome are not revisable by this addendum or by any later one."
    /// A threshold a caller could pass on the command line is a threshold
    /// that can be moved after seeing the numbers, which is the one thing the
    /// pre-registration exists to prevent. Changing it means editing this
    /// line in a commit that a reviewer can date against the verdicts.
    public const int FalseFlagCeilingPercent = 25;

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
        md.AppendLine("## The selection criteria");
        md.AppendLine();
        md.AppendLine(
            "`eval/judge/DECISION-RULE-V2.md`, committed before any `" + RubricVersion
            + "` configuration judged a pair. A configuration is eligible only if its false-flag "
            + "rate is at most " + FalseFlagCeilingPercent.ToString(CultureInfo.InvariantCulture)
            + "%; among eligible configurations the order is lowest missed-ungrounded, then lowest "
            + "false-flag, then cheapest. If none is eligible, none is adopted and no groundedness "
            + "score is published.");
        md.AppendLine();
        md.AppendLine("| configuration | missed-ungrounded | false-flag | eligible |");
        md.AppendLine("|---|---|---|---|");

        foreach (var c in Configurations)
        {
            var eligible = c.Agreement.FalseFlag.AtOrUnder(FalseFlagCeilingPercent);

            md.AppendLine(
                $"| `{c.Model}` / {c.Effort} | {Rate(c.Agreement.MissedUngrounded)} | "
                + $"{Rate(c.Agreement.FalseFlag)} | "
                + (eligible
                    ? "yes |"
                    : $"**no** - false-flag over the {FalseFlagCeilingPercent.ToString(CultureInfo.InvariantCulture)}% ceiling |"));
        }

        md.AppendLine();
        md.AppendLine(
            "**missed-ungrounded** is over the pairs the labels call ungrounded; **false-flag** is "
            + "over the pairs the labels call grounded. Two different denominators, because the two "
            + "rates answer questions about two different sets of drafts - averaging them is what "
            + "the v1 rule did, and it named as winner the configuration most likely to certify the "
            + "failure the measurement exists to catch.");
        md.AppendLine();
        md.AppendLine(
            "Intervals are Wilson at 95%. The ceiling is applied to the **point estimate**, as the "
            + "rule says in terms: at these denominators the intervals are wide enough that testing "
            + "the bound instead would let almost anything through.");
        md.AppendLine();
        md.AppendLine(
            "This table reports the rates and eligibility. **It does not name a winner.** The third "
            + "tiebreak is cost, which is null for any model absent from the pricing file, and a "
            + "selection that silently depended on how complete that file happened to be would be "
            + "worse than three ordered comparisons a reader can make from the rows above.");

        md.AppendLine();
        md.AppendLine("### McNemar, restricted to the ungrounded-labelled pairs");
        md.AppendLine();
        md.AppendLine("| configuration | baseline only correct | this only correct | p | |");
        md.AppendLine("|---|---|---|---|---|");

        foreach (var (config, result) in AgainstBaselineUngroundedOnly)
        {
            md.AppendLine(McNemarRow(config, result));
        }

        md.AppendLine();
        md.AppendLine(
            "The comparison the primary criterion rests on, and the only one the rule asks for: "
            + "restricted to pairs the labels call ungrounded, so it speaks to missed-ungrounded "
            + "alone. The all-pairs version below mixes both error directions.");

        md.AppendLine();
        md.AppendLine("## Reported, not decisive");
        md.AppendLine();
        md.AppendLine(
            "Overall agreement, its interval and Cohen's kappa describe each configuration. They "
            + "are not used to select, because averaging the two error directions is what produced "
            + "the v1 result.");

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
            $"### Against the baseline (`{Configurations[0].Model}` / {Configurations[0].Effort}), all labelled pairs");
        md.AppendLine();
        md.AppendLine("| configuration | baseline only correct | this only correct | p | |");
        md.AppendLine("|---|---|---|---|---|");

        foreach (var (config, result) in AgainstBaseline)
        {
            md.AppendLine(McNemarRow(config, result));
        }

        md.AppendLine();
        md.AppendLine("McNemar's exact test on discordant pairs, two-sided, alpha 0.05. Only items where "
            + "exactly one configuration matched the label carry information; items both matched, or "
            + "both missed, are excluded by construction.");

        return md.ToString();
    }

    /// Where a comparison run may write its table, or null for "print only".
    ///
    /// **There is deliberately no default.** The default used to be
    /// `eval/judge`, which holds `COMPARISON.md` - the committed judge-v1
    /// result, named in DECISION-RULE.md as the record that "stands as
    /// recorded". Any later comparison run without `--output` would have
    /// replaced it and said `Wrote ...`.
    ///
    /// That is the same trap `DevCheck.ResolveReportPath` was written for,
    /// one command over. The shape is copied rather than shared: two call
    /// sites with different filenames and different explanations is not yet
    /// an abstraction, and the alternative was editing a guard that already
    /// has passing tests in order to add a second caller.
    ///
    /// An existing table is never overwritten either. Comparisons are how a
    /// superseded rule stays readable - v1's result is evidence for why v2
    /// exists - so replacing one silently destroys the thing the next
    /// comparison is argued against.
    ///
    /// The console already carries the whole table, so a run that writes
    /// nothing loses nothing.
    public static string? ResolveReportPath(string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return null;
        }

        var path = Path.Combine(outputDirectory, "COMPARISON.md");

        if (File.Exists(path))
        {
            throw new InvalidOperationException(
                $"'{Path.GetFullPath(path)}' already exists and would be overwritten. A comparison "
                + "table is the record of one candidate set judged under one rubric; replacing it "
                + "silently loses the result a later rule is argued against. Point --output at a "
                + "different directory, or remove that file deliberately.");
        }

        return path;
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

        // The same test over the ungrounded-labelled pairs only, which is the
        // one DECISION-RULE-V2.md asks for: "McNemar's exact test is reported
        // between configurations restricted to ungrounded-labelled pairs,
        // which is the comparison the primary criterion rests on."
        //
        // Restricting the labels is the whole of it - McNemar already drops
        // any pair it has no label for, so a filtered dictionary gives the
        // restricted test without a second implementation to keep in step.
        var ungroundedLabels = labels
            .Where(l => !l.Value)
            .ToDictionary(l => l.Key, l => l.Value);

        var againstUngrounded = verdictFiles
            .Skip(1)
            .Select(f => (
                Config: $"`{f.Model}` / {f.Effort}",
                Result: McNemar.Compare(ungroundedLabels, baseline, ToMap(f))))
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
            againstUngrounded,
            pricing?.Currency ?? "USD");
    }

    /// "4/25 = 16.0% [6, 35]" - the count, the denominator, the point
    /// estimate and the interval, in that order.
    private static string Rate(ErrorRate rate) =>
        $"{rate.Count}/{rate.Denominator} = {Percent(rate.Value, 1)}% "
        + $"[{Percent(rate.Lower, 0)}, {Percent(rate.Upper, 0)}]";

    private static string McNemarRow(string config, McNemarResult result) =>
        $"| {config} | {result.OnlyFirstCorrect} | {result.OnlySecondCorrect} | "
        + $"{result.PValue.ToString("F4", CultureInfo.InvariantCulture)} | "
        + $"{(result.Significant ? "**significant**" : "not significant")} |";

    private static string Percent(double value, int decimals) =>
        (value * 100).ToString($"F{decimals}", CultureInfo.InvariantCulture);

    private static Dictionary<string, bool> ToMap(JudgeVerdictFile file) =>
        file.Verdicts
            .Where(v => v.Grounded is not null)
            .ToDictionary(v => v.QueryId, v => v.Grounded!.Value);
}
