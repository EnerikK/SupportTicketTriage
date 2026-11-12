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

    /// The two rates DECISION-RULE-V2.md selects on, and the mistake that
    /// would make both of them wrong in the same direction: dividing by the
    /// number of pairs compared instead of by the pairs each rate is about.
    ///
    /// They have different denominators by construction. missed-ungrounded is
    /// over what the labels call ungrounded; false-flag is over what the
    /// labels call grounded. Over 93 pairs the wrong denominator produces a
    /// smaller, friendlier-looking number for both.
    [Fact]
    public void TheTwoErrorRates_UseTheirOwnDenominators()
    {
        // Labels: a, b, c grounded; d, e ungrounded.
        // Judge:  b flagged (one false flag of three), d passed (one miss of two).
        var agreement = GroundednessMetrics.Compare(
            [Judged("a", true), Judged("b", false), Judged("c", true),
             Judged("d", true), Judged("e", false)],
            new Dictionary<string, bool>
            {
                ["a"] = true, ["b"] = true, ["c"] = true, ["d"] = false, ["e"] = false,
            });

        Assert.Equal(5, agreement.Compared);

        Assert.Equal(1, agreement.MissedUngrounded.Count);
        Assert.Equal(2, agreement.MissedUngrounded.Denominator);
        Assert.Equal(0.5, agreement.MissedUngrounded.Value, 10);

        Assert.Equal(1, agreement.FalseFlag.Count);
        Assert.Equal(3, agreement.FalseFlag.Denominator);
        Assert.Equal(1.0 / 3, agreement.FalseFlag.Value, 10);
    }

    /// "At most 25%" includes 25%.
    ///
    /// Not hypothetical: the judge-v2 held-out run put two configurations on
    /// exactly 17/68, and whether they were eligible decided the comparison.
    [Fact]
    public void AnErrorRateExactlyOnTheCeiling_IsEligible()
    {
        Assert.True(GroundednessMetrics.RateOf(17, 68).AtOrUnder(25));
        Assert.False(GroundednessMetrics.RateOf(18, 68).AtOrUnder(25));
    }

    /// The ceiling test is integer arithmetic, not a comparison of two
    /// doubles. 1/3 is not representable in binary, and a rate that is just
    /// over a ceiling must not pass because the quotient rounded down.
    [Theory]
    [InlineData(1, 3, 33, false)]     // 33.33% - over 33%
    [InlineData(2, 3, 67, true)]      // 66.67% - under 67%
    [InlineData(33, 100, 33, true)]   // exactly on it
    [InlineData(1, 7, 14, false)]     // 14.28% - over 14%
    [InlineData(0, 0, 25, true)]      // nothing measured: 0 <= 0, and no divide
    public void TheCeilingTest_DoesNotGoThroughAFloatingPointQuotient(
        int count, int denominator, int ceiling, bool eligible)
    {
        Assert.Equal(eligible, GroundednessMetrics.RateOf(count, denominator).AtOrUnder(ceiling));
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

    /// The ceiling ADR-037 froze. A test that fails when somebody edits the
    /// constant is the point: "the 25% false-flag ceiling, the ordering, and
    /// the no-eligible-configuration outcome are not revisable by this
    /// addendum or by any later one." Moving it should cost a red test and a
    /// reading of why it was 25.
    [Fact]
    public void TheFalseFlagCeiling_IsTheOneTheDecisionRuleFroze()
    {
        Assert.Equal(25, JudgeComparisonReport.FalseFlagCeilingPercent);
    }

    /// The restricted McNemar is a different test, not a relabelled one.
    ///
    /// DECISION-RULE-V2.md asks for it over the ungrounded-labelled pairs
    /// because that is where the primary criterion lives. Here the two
    /// configurations are built to disagree on every pair, so the all-pairs
    /// test sees four discordant items and the restricted one sees two - if
    /// the restriction were dropped the two tables would be identical and
    /// nothing in the document would say so.
    [Fact]
    public void TheUngroundedOnlyMcNemar_IgnoresGroundedLabelledPairs()
    {
        var report = JudgeComparisonReport.Build(
            Provenance(),
            new Dictionary<string, bool> { ["0"] = false, ["1"] = false, ["2"] = true, ["3"] = true },
            [
                Verdicts("expensive", JudgeReasoningEffort.Default, false, true, true, false),
                Verdicts("cheap", JudgeReasoningEffort.Low, true, false, false, true),
            ],
            Pricing());

        var all = Assert.Single(report.AgainstBaseline).Result;
        var ungrounded = Assert.Single(report.AgainstBaselineUngroundedOnly).Result;

        Assert.Equal(4, all.Discordant);
        Assert.Equal(2, all.OnlyFirstCorrect);
        Assert.Equal(2, all.OnlySecondCorrect);

        Assert.Equal(2, ungrounded.Discordant);
        Assert.Equal(1, ungrounded.OnlyFirstCorrect);
        Assert.Equal(1, ungrounded.OnlySecondCorrect);
    }

    /// Eligibility is in the table, named, with the reason.
    ///
    /// A reader applying the rule needs to see which configurations the first
    /// step removed, not recompute it from two percentages and a sentence
    /// somewhere else in the document.
    [Fact]
    public void TheTable_SaysWhichConfigurationsTheCeilingExcludes()
    {
        var report = JudgeComparisonReport.Build(
            Provenance(),
            new Dictionary<string, bool>
            {
                ["0"] = true, ["1"] = true, ["2"] = true, ["3"] = true, ["4"] = false,
            },
            [
                // Flags two of four grounded drafts: 50%, over the ceiling.
                Verdicts("expensive", JudgeReasoningEffort.Default, false, false, true, true, false),
                // Flags none: 0%, eligible - and it misses the one ungrounded
                // draft, which is exactly what the ceiling alone cannot catch.
                Verdicts("cheap", JudgeReasoningEffort.Low, true, true, true, true, true),
            ],
            Pricing());

        var over = report.Configurations[0].Agreement.FalseFlag;
        var under = report.Configurations[1].Agreement.FalseFlag;

        Assert.Equal(4, over.Denominator);
        Assert.False(over.AtOrUnder(JudgeComparisonReport.FalseFlagCeilingPercent));
        Assert.True(under.AtOrUnder(JudgeComparisonReport.FalseFlagCeilingPercent));

        var markdown = report.ToMarkdown();

        Assert.Contains("| 2/4 = 50.0% [", markdown);
        Assert.Contains("false-flag over the 25% ceiling", markdown);

        // And the rule's own words, so the table carries the criterion it is
        // read against rather than assuming the reader has the file open.
        Assert.Contains("lowest missed-ungrounded", markdown);
        Assert.Contains("no groundedness score is published", markdown);
    }
}
