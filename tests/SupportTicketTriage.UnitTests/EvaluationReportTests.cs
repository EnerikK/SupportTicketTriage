using System.Globalization;
using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Pricing;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

/// The Markdown report is an artifact: it gets compared between runs on
/// different machines and pasted into the README. It must therefore render
/// identically regardless of who ran it, which is a property of the report
/// rather than of the process that happened to produce it.
public class EvaluationReportTests
{
    private static EvaluationReport Report() =>
        new(
            DatasetVersion: "1.0",
            EmbeddingModel: "text-embedding-3-small",
            EmbeddingModelIsReal: true,
            ChatModel: "gpt-4.1-mini",
            GeneratedAt: new DateTimeOffset(2026, 10, 8, 17, 6, 52, TimeSpan.Zero),
            CorpusCount: 240,
            QueryCount: 60,
            ScoredQueryCount: 54,
            UnscoredQueryCount: 6,
            RecallAtK: new Dictionary<int, double> { [1] = 0.7592592592592593, [3] = 0.8981481481481481 },
            Classification: null,
            Citations: null,
            Calibration: [],
            Latency: [],
            Cost: null,
            TraitCoverage: new Dictionary<string, int> { ["hard-negative"] = 75 });

    /// Runs the renderer under a comma-decimal culture, which is what this was
    /// developed on and what produced "75,9%" in a real report. Asserting the
    /// output rather than setting a process-wide culture is deliberate: a
    /// global `DefaultThreadCurrentCulture` is ambient state any caller can
    /// undo, and a test that set its own culture would silently defeat it.
    /// Invariant formatting at the point of use is a property nothing can
    /// override, and this test proves it by trying to.
    [Theory]
    [InlineData("el-GR")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void RecallRendersWithADecimalPoint_WhateverTheAmbientCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var markdown = Report().ToMarkdown();

            Assert.Contains("75.9", markdown);
            Assert.DoesNotContain("75,9", markdown);
            Assert.Contains("89.8", markdown);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// Pins the whole table, so a later change to the format specifier is a
    /// visible decision rather than a silent one.
    [Fact]
    public void TheRecallTableIsOrderedByKAndFormattedToOneDecimal()
    {
        var markdown = Report().ToMarkdown();

        Assert.Contains("| 1 | 75.9% |", markdown);
        Assert.Contains("| 3 | 89.8% |", markdown);
        Assert.True(
            markdown.IndexOf("| 1 | 75.9% |", StringComparison.Ordinal)
                < markdown.IndexOf("| 3 | 89.8% |", StringComparison.Ordinal),
            "Recall rows should be ordered by k.");
    }

    /// The stand-in embedder cannot produce a baseline, and the report says so
    /// in the document itself rather than only refusing at write time.
    [Fact]
    public void AStandInEmbedderIsCalledOutInTheReportItself()
    {
        var standIn = Report() with { EmbeddingModel = "fake-lexical-v1", EmbeddingModelIsReal = false };

        Assert.Contains("not a baseline", standIn.ToMarkdown());
        Assert.DoesNotContain("not a baseline", Report().ToMarkdown());
    }

    /// The calibration section carries the evidence a threshold decision is
    /// made from, including the scores of observations whose outcome is
    /// undefined - those are not evidence of where the floor belongs, but a
    /// floor above all of them refuses every unanswerable query, which is the
    /// clearest signal the table has.
    [Fact]
    public void TheCalibrationSectionNamesTheFloorThatRefusesEveryUndefinedCase()
    {
        var gate = new GateCalibration(
            Gate: "B - retrieval similarity",
            ScoreName: "top-1 cosine similarity",
            SuccessName: "a relevant ticket in the top 5",
            Separation: 0.141,
            AbsentScore: 0,
            Floors: CalibrationMetrics.AtEachFloor(
                [new CalibrationCase("q1", 0.72, true), new CalibrationCase("q2", 0.40, false)],
                [0.60]),
            UndefinedOutcomeScores: [0.258, 0.531, 0.598]);

        var markdown = (Report() with { Calibration = [gate] }).ToMarkdown();

        Assert.Contains("Gate calibration", markdown);
        Assert.Contains("Separation: 0.141", markdown);
        Assert.Contains("a floor above 0.598 refuses all of them", markdown);
        // Invariant formatting here too - this table is diffed between runs.
        Assert.DoesNotContain("0,598", markdown);
    }

    [Fact]
    public void TheCalibrationSectionIsOmittedWhenThereIsNothingToCalibrate()
    {
        Assert.DoesNotContain("Gate calibration", Report().ToMarkdown());
    }

    /// A report must not state something false about the run that produced it.
    ///
    /// `Citations` is null for two different reasons - no chat deployment, or
    /// `--skip-generation` with one present - and the renderer used to claim
    /// the first unconditionally. That made the report contradict its own
    /// header, which names the chat deployment it had just said did not exist.
    ///
    /// The report built here is the exact shape `--skip-generation` produces
    /// against a real deployment: classification scored, citations not.
    [Fact]
    public void SkippedGenerationIsReportedAsSkippedRatherThanAsAMissingDeployment()
    {
        var markdown = (Report() with { Classification = Classified() }).ToMarkdown();

        Assert.Contains("`gpt-4.1-mini`", markdown);
        Assert.Contains("generation was skipped", markdown);
        Assert.DoesNotContain("this run had no chat deployment", markdown);
    }

    /// The other half of the same branch: with no chat deployment at all, the
    /// original message is the true one and must survive.
    [Fact]
    public void AMissingChatDeploymentIsStillReportedAsSuch()
    {
        var markdown = (Report() with { ChatModel = null }).ToMarkdown();

        Assert.Contains("this run had no chat deployment", markdown);
        Assert.DoesNotContain("generation was skipped", markdown);
    }

    private static ClassificationScore Classified() =>
        new(
            Accuracy: 0.92,
            MacroF1: 0.928,
            Total: 300,
            Unclassified: 5,
            PerLabel: [new LabelScore("Billing", 0.94, 0.94, 0.94, 50)]);

    private static CostScore Costed(bool verified) =>
        new(
            PricingVersion: "1.0",
            Currency: "USD",
            PricingVerified: verified,
            PricingRetrievedOn: new DateOnly(2026, 10, 16),
            PerStage:
            [
                new StageCost(
                    AiStage.Classification, "gpt-4.1-mini", 1_234_567, 89_012, 1.2345m, UnpricedCalls: 0),
            ],
            Total: 1.2345m,
            PerIngestedTicket: 0.0041m,
            PerDraftedTicket: 0.0262m,
            TicketCount: 300,
            DraftCount: 47);

    private static EvaluationReport Measured(bool verified = true) =>
        Report() with
        {
            Latency =
            [
                new StageLatency(
                    AiStage.Generation,
                    Calls: 47,
                    Failures: 1,
                    // Deliberately not .5: "F0" rounds a midpoint to even, so
                    // 1234.5 renders as "1234". That is fine for a latency
                    // figure - nobody reads sub-millisecond precision - but
                    // this test is about the decimal separator, and a midpoint
                    // would make it fail for an unrelated reason.
                    P50: TimeSpan.FromMilliseconds(1234.4),
                    P95: TimeSpan.FromMilliseconds(2345.6),
                    TotalElapsed: TimeSpan.FromSeconds(61.75)),
            ],
            Cost = Costed(verified),
        };

    /// The same artifact argument as recall: these tables get diffed between
    /// runs and pasted into the README, so a comma-decimal machine must not
    /// render them differently from CI.
    [Theory]
    [InlineData("el-GR")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void LatencyAndCostRenderWithDecimalPoints_WhateverTheAmbientCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var markdown = Measured().ToMarkdown();

            Assert.Contains("1234 ms", markdown);
            Assert.Contains("2346 ms", markdown);
            Assert.Contains("1.2345", markdown);
            Assert.Contains("0.0262", markdown);
            Assert.Contains("1,234,567", markdown);

            Assert.DoesNotContain("1,2345", markdown);
            Assert.DoesNotContain("0,0262", markdown);
            Assert.DoesNotContain("1.234.567", markdown);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// Four decimal places is enough for a stage total and not for a per-ticket
    /// figure: the real measured value of 0.000149 renders as "0.0001" at F4,
    /// which discards a third of it and invites multiplying the wrong number.
    [Fact]
    public void PerTicketCostsAreRenderedWithEnoughDecimalsToBeMultipliedBy()
    {
        var measured = Measured() with
        {
            Cost = Costed(verified: true) with
            {
                PerIngestedTicket = 0.000149m,
                PerDraftedTicket = 0.000796m,
            },
        };

        var markdown = measured.ToMarkdown();

        Assert.Contains("0.000149", markdown);
        Assert.Contains("0.000796", markdown);

        // And the unit a reader actually reasons in.
        Assert.Contains("Per 1,000 drafted tickets: **0.7960**", markdown);
    }

    /// Unverified prices must announce themselves. The figures are arithmetic
    /// against numbers nobody checked, and the whole repository's claim is that
    /// it does not publish figures it cannot stand behind.
    [Fact]
    public void UnverifiedPricingCarriesAWarningInTheReport()
    {
        var markdown = Measured(verified: false).ToMarkdown();

        Assert.Contains("These costs are not a result", markdown);
        Assert.Contains("verified", markdown);
    }

    /// The top-of-report banner disclaims retrieval quality, which is a
    /// different claim. A stand-in embedder returns in microseconds, so "0 ms"
    /// reads as fast when it means "not a deployment".
    [Fact]
    public void AStandInEmbedderDisclaimsItsOwnLatency()
    {
        var standIn = Measured() with
        {
            EmbeddingModel = "fake-lexical-v1",
            EmbeddingModelIsReal = false,
        };

        Assert.Contains("Embedding latency here is meaningless", standIn.ToMarkdown());
        Assert.DoesNotContain("Embedding latency here is meaningless", Measured().ToMarkdown());
    }

    [Fact]
    public void VerifiedPricingCarriesNoWarning()
    {
        Assert.DoesNotContain("These costs are not a result", Measured().ToMarkdown());
    }

    [Fact]
    public void WithNoPricingFileTheCostSectionSaysSoRatherThanReportingZero()
    {
        var markdown = Report().ToMarkdown();

        Assert.Contains("no pricing file was loaded", markdown);
        Assert.DoesNotContain("0.0000", markdown);
    }

    /// A latency section is omitted entirely rather than rendered empty, the
    /// same way a skipped stage is omitted from the table.
    [Fact]
    public void WithNoRecordedCallsThereIsNoLatencySection()
    {
        Assert.DoesNotContain("## Latency", Report().ToMarkdown());
    }

    /// The money half of the baseline guard. Prices nobody has checked would
    /// make the regression gate compare against an assumption.
    [Fact]
    public void ABaselineIsRefusedWhenTheCostsCameFromUnverifiedPricing()
    {
        var error = Assert.Throws<InvalidOperationException>(
            Measured(verified: false).EnsureCanBeBaseline);

        Assert.Contains("unverified pricing", error.Message);
        Assert.Contains("2026-10-16", error.Message);
    }

    [Fact]
    public void ABaselineIsAllowedWhenPricingIsVerifiedAndTheEmbedderIsReal()
    {
        Measured().EnsureCanBeBaseline();
    }

    /// A run with no costing at all is still a valid baseline - latency and
    /// cost are additions, not preconditions.
    [Fact]
    public void ABaselineIsAllowedWithNoCostSectionAtAll()
    {
        Report().EnsureCanBeBaseline();
    }
}
