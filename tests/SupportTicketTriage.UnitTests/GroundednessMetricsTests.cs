using System.Globalization;
using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Eval.Pricing;
using SupportTicketTriage.Eval.Reporting;
using SupportTicketTriage.Infrastructure.Ai;
using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class GroundednessMetricsTests
{
    private static GroundednessCase Judged(string id, bool? grounded) =>
        new(id, grounded, grounded == false ? "something unsupported" : null);

    [Fact]
    public void GroundedRate_IsOverJudgedDraftsOnly()
    {
        var score = GroundednessMetrics.Summarise(
        [
            Judged("a", true),
            Judged("b", true),
            Judged("c", false),
            Judged("d", null),
        ]);

        Assert.Equal(3, score.Judged);
        Assert.Equal(1, score.Unjudged);
        Assert.Equal(2.0 / 3, score.GroundedRate, 10);
    }

    /// A judge outage is not evidence about a draft. Counting it as ungrounded
    /// would make the score fall whenever the judge deployment wobbled, which
    /// measures availability and reports it as quality.
    [Fact]
    public void AnUnjudgedDraft_DoesNotDepressTheScore()
    {
        var withOutage = GroundednessMetrics.Summarise([Judged("a", true), Judged("b", null)]);
        var without = GroundednessMetrics.Summarise([Judged("a", true)]);

        Assert.Equal(without.GroundedRate, withOutage.GroundedRate);
        Assert.Equal(1, withOutage.Unjudged);
    }

    [Fact]
    public void Agreement_CountsEachQuadrant()
    {
        var agreement = GroundednessMetrics.Compare(
            [Judged("a", true), Judged("b", false), Judged("c", true), Judged("d", false)],
            new Dictionary<string, bool> { ["a"] = true, ["b"] = false, ["c"] = false, ["d"] = true });

        Assert.Equal(4, agreement.Compared);
        Assert.Equal(1, agreement.BothGrounded);
        Assert.Equal(1, agreement.BothUngrounded);
        Assert.Equal(1, agreement.JudgeGroundedHumanNot);
        Assert.Equal(1, agreement.HumanGroundedJudgeNot);
        Assert.Equal(0.5, agreement.RawAgreement, 10);
    }

    /// The reason kappa is reported at all, and the case a bare agreement
    /// figure would have published as a success.
    ///
    /// Nine of ten drafts are grounded and the judge answers "grounded" every
    /// time. It agrees with the human 90% of the time while carrying no
    /// information whatsoever, and kappa says so.
    [Fact]
    public void AConstantJudge_ScoresHighAgreementAndZeroKappa()
    {
        var ids = Enumerable.Range(0, 10).Select(i => i.ToString()).ToList();
        var judged = ids.Select(id => Judged(id, true)).ToList();
        var human = ids.ToDictionary(id => id, id => id != "9");

        var agreement = GroundednessMetrics.Compare(judged, human);

        Assert.Equal(0.9, agreement.RawAgreement, 10);
        Assert.Equal(0, agreement.Kappa, 10);
    }

    [Fact]
    public void PerfectAgreement_IsKappaOne()
    {
        var agreement = GroundednessMetrics.Compare(
            [Judged("a", true), Judged("b", false)],
            new Dictionary<string, bool> { ["a"] = true, ["b"] = false });

        Assert.Equal(1, agreement.RawAgreement, 10);
        Assert.Equal(1, agreement.Kappa, 10);
    }

    /// Both sides labelled everything the same way, so chance agreement is 1
    /// and kappa is 0/0. A NaN reaching a published report would be worse than
    /// either answer.
    [Fact]
    public void EveryoneAgreesOnEverything_DoesNotProduceNaN()
    {
        var agreement = GroundednessMetrics.Compare(
            [Judged("a", true), Judged("b", true)],
            new Dictionary<string, bool> { ["a"] = true, ["b"] = true });

        Assert.Equal(1, agreement.Kappa, 10);
        Assert.False(double.IsNaN(agreement.Kappa));
    }

    /// An unlabelled pair is skipped, not scored. Stopping halfway through the
    /// file must not read as a run of disagreements.
    [Fact]
    public void UnlabelledAndUnjudgedPairs_AreDroppedFromTheComparison()
    {
        var agreement = GroundednessMetrics.Compare(
            [Judged("a", true), Judged("b", null), Judged("c", true)],
            new Dictionary<string, bool> { ["a"] = true });

        Assert.Equal(1, agreement.Compared);
    }

    /// Wilson rather than the normal approximation, and this is why.
    ///
    /// At 46/47 the normal interval runs past 1.0 - an agreement figure
    /// claiming an upper bound above certainty. Wilson stays inside the range
    /// by construction, which matters because near-perfect agreement is
    /// exactly where this metric is expected to live.
    [Fact]
    public void TheAgreementIntervalStaysInsideZeroToOne()
    {
        var ids = Enumerable.Range(0, 47).Select(i => i.ToString()).ToList();
        var judged = ids.Select(id => Judged(id, true)).ToList();
        var human = ids.ToDictionary(id => id, id => id != "46");

        var agreement = GroundednessMetrics.Compare(judged, human);

        Assert.True(agreement.AgreementUpper <= 1.0, $"upper was {agreement.AgreementUpper}");
        Assert.True(agreement.AgreementLower >= 0.0);
        Assert.InRange(agreement.RawAgreement, agreement.AgreementLower, agreement.AgreementUpper);
    }

    /// A small sample must not report a narrow interval. Five pairs cannot
    /// pin agreement to within a few points, and an interval that said
    /// otherwise would invite exactly the over-reading the figure exists to
    /// prevent.
    [Fact]
    public void ASmallSampleProducesAWideInterval()
    {
        var ids = Enumerable.Range(0, 5).Select(i => i.ToString()).ToList();

        var agreement = GroundednessMetrics.Compare(
            ids.Select(id => Judged(id, true)).ToList(),
            ids.ToDictionary(id => id, _ => true));

        Assert.True(
            agreement.AgreementUpper - agreement.AgreementLower > 0.3,
            "five pairs should not produce a tight interval");
    }
}

public class McNemarTests
{
    private static Dictionary<string, bool> Map(params bool[] values) =>
        values.Select((v, i) => (i, v)).ToDictionary(x => x.i.ToString(), x => x.v);

    /// Identical mistakes carry no information about which is better.
    [Fact]
    public void NoDiscordantPairsIsNoEvidenceOfADifference()
    {
        var labels = Map(true, true, false, false);

        var result = McNemar.Compare(labels, Map(true, false, false, false), Map(true, false, false, false));

        Assert.Equal(0, result.Discordant);
        Assert.Equal(1.0, result.PValue);
        Assert.False(result.Significant);
    }

    /// The case the rule is written for: a handful of disagreements, where
    /// the chi-squared approximation is not trustworthy and the exact test is.
    ///
    /// Three discordant pairs all favouring one side gives an exact two-sided
    /// p of 2 * (1/8) = 0.25 - not significant, and correctly so. A naive
    /// "3 versus 0 looks decisive" reading is what this guards against.
    [Fact]
    public void ThreeDiscordantPairsOneWayAreNotSignificant()
    {
        var labels = Map(true, true, true);

        var result = McNemar.Compare(labels, Map(true, true, true), Map(false, false, false));

        Assert.Equal(3, result.OnlyFirstCorrect);
        Assert.Equal(0, result.OnlySecondCorrect);
        Assert.Equal(0.25, result.PValue, 10);
        Assert.False(result.Significant);
    }

    /// Six one-way disagreements: 2 * (1/64) = 0.03125, below alpha.
    [Fact]
    public void SixDiscordantPairsOneWayAreSignificant()
    {
        var labels = Map(true, true, true, true, true, true);

        var result = McNemar.Compare(
            labels,
            Map(true, true, true, true, true, true),
            Map(false, false, false, false, false, false));

        Assert.Equal(0.03125, result.PValue, 10);
        Assert.True(result.Significant);
    }

    /// Evenly split disagreements are the clearest "no difference" case and
    /// must never come out significant.
    [Fact]
    public void EvenlySplitDisagreementsAreNotSignificant()
    {
        var labels = Map(true, true, true, true);

        var result = McNemar.Compare(labels, Map(true, true, false, false), Map(false, false, true, true));

        Assert.Equal(2, result.OnlyFirstCorrect);
        Assert.Equal(2, result.OnlySecondCorrect);
        Assert.Equal(1.0, result.PValue, 10);
        Assert.False(result.Significant);
    }

    /// Items only one configuration judged are dropped: a verdict the other
    /// never produced cannot be a disagreement between them.
    [Fact]
    public void ItemsMissingFromEitherConfigurationAreIgnored()
    {
        var labels = Map(true, true, true);
        var first = Map(true, true, true);
        var second = new Dictionary<string, bool> { ["0"] = false };

        var result = McNemar.Compare(labels, first, second);

        Assert.Equal(1, result.Discordant);
    }
}

/// The table that gets pasted into a document, formatted so two machines
/// produce the same bytes.
public class JudgeComparisonReportTests
{
    private static JudgeVerdictFile Verdicts(string model, JudgeReasoningEffort effort, params bool[] grounded) =>
        new(model, GroundednessPrompt.Version, GroundednessPrompt.Sha256, GenerationPrompt.Sha256,
            effort, DateTimeOffset.UnixEpoch,
            grounded.Select((g, i) => new JudgeVerdict(i.ToString(), g, null, 100, 200, null)).ToList());

    private static LabelProvenance Provenance() =>
        new("eval/judge/pairs.json", new string('a', 64), new string('b', 40), DateTimeOffset.UnixEpoch);

    private static ModelPricing Pricing() =>
        new("test", "USD", "test", true,
        [
            new ModelPrice("expensive", 1.25m, 10.00m, new DateOnly(2026, 10, 30), "GlobalStandard"),
            new ModelPrice("cheap", 0.25m, 2.00m, new DateOnly(2026, 10, 30), "GlobalStandard"),
        ]);

    /// ADR-017, and a defect this repository has now shipped twice: a report
    /// formatted with the ambient culture renders "54,3%" on a Greek machine
    /// and "54.3%" in CI, so two runs of identical data produce different
    /// bytes and nothing downstream can compare them.
    [Fact]
    public void EveryNumberIsFormattedInvariantlyWhateverTheAmbientCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("el-GR");

            var report = JudgeComparisonReport.Build(
                Provenance(),
                new Dictionary<string, bool> { ["0"] = true, ["1"] = true, ["2"] = false },
                [
                    Verdicts("expensive", JudgeReasoningEffort.Default, true, false, false),
                    Verdicts("cheap", JudgeReasoningEffort.Low, true, true, false),
                ],
                Pricing());

            var markdown = report.ToMarkdown();

            Assert.DoesNotContain(",0", markdown);
            Assert.DoesNotContain(",3", markdown);
            Assert.DoesNotContain("¤", markdown);
            Assert.Contains("66.7%", markdown);
            Assert.Contains("USD", markdown);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    /// An unpriced configuration must never render as the cheapest one.
    [Fact]
    public void AConfigurationMissingFromThePricingFileReadsAsNotPriced()
    {
        var report = JudgeComparisonReport.Build(
            Provenance(),
            new Dictionary<string, bool> { ["0"] = true },
            [
                Verdicts("expensive", JudgeReasoningEffort.Default, true),
                Verdicts("nobody-priced-this", JudgeReasoningEffort.Default, true),
            ],
            Pricing());

        Assert.Contains("not priced", report.ToMarkdown());
    }

    /// A judged pair with no label is excluded and named, rather than
    /// silently counted as agreement or disagreement.
    [Fact]
    public void AJudgedButUnlabelledPairIsExcludedAndNamed()
    {
        var report = JudgeComparisonReport.Build(
            Provenance(),
            new Dictionary<string, bool> { ["0"] = true },
            [
                Verdicts("expensive", JudgeReasoningEffort.Default, true, true),
                Verdicts("cheap", JudgeReasoningEffort.Default, true, true),
            ],
            Pricing());

        Assert.Equal(2, report.VerdictCount);
        Assert.Equal(1, report.LabelCount);
        Assert.Equal("1", Assert.Single(report.JudgedButUnlabelled));
        Assert.Contains("`1`", report.ToMarkdown());
    }
}
