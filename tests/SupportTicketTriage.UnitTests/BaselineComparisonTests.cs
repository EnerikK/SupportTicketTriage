using SupportTicketTriage.Eval.Baseline;
using SupportTicketTriage.Eval.Metrics;
using SupportTicketTriage.Eval.Reporting;

namespace SupportTicketTriage.UnitTests;

public class BaselineComparisonTests
{
    private static EvaluationReport Run(
        double recall1 = 0.76,
        double? accuracy = 0.92,
        double? macroF1 = 0.93,
        double? citationValidity = 1.0,
        decimal? costTotal = 0.075m,
        string? classificationPrompt = "classify-v1",
        string redaction = "v1",
        double? gateA = 0.50,
        string? generationPrompt = "draft-v1",
        string? chatModel = "gpt-4.1-mini",
        string pricingVersion = "1.0",
        string? holdout = null) =>
        new(
            DatasetVersion: "1.0",
            EmbeddingModel: "text-embedding-3-small",
            EmbeddingModelIsReal: true,
            ChatModel: chatModel,
            ClassificationPromptVersion: classificationPrompt,
            GenerationPromptVersion: generationPrompt,
            RedactionVersion: redaction,
            MinimumClassificationScore: gateA,
            MinimumSimilarity: 0.60,
            GeneratedAt: DateTimeOffset.UnixEpoch,
            CorpusCount: 240,
            QueryCount: 60,
            ScoredQueryCount: 54,
            UnscoredQueryCount: 6,
            HoldoutQuerySource: holdout,
            RecallAtK: new Dictionary<int, double> { [1] = recall1 },
            Classification: accuracy is null || macroF1 is null
                ? null
                : new ClassificationScore(accuracy.Value, macroF1.Value, 300, 5, []),
            Citations: citationValidity is null
                ? null
                : new CitationScore(citationValidity.Value, 47, 47, 0, 13, 0, 75, 0),
            Groundedness: null,
            JudgeAgreement: null,
            JudgeModel: null,
            JudgeRubricVersion: null,
            JudgeReasoningEffort: null,
            LabelSha256: null,
            LabelCommit: null,
            Calibration: [],
            Latency: [],
            Cost: costTotal is null
                ? null
                : new CostScore(pricingVersion, "USD", true, new DateOnly(2026, 10, 16), [], costTotal, 0.0001m, 0.0008m, 300, 47),
            TraitCoverage: new Dictionary<string, int>());

    private static EvaluationBaseline Baseline() => EvaluationBaseline.From(Run());

    private static MetricComparison Metric(BaselineComparison c, string name) =>
        Assert.Single(c.Metrics, m => m.Metric == name);

    [Fact]
    public void AnIdenticalRunPasses()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run());

        Assert.True(comparison.Passed);
        Assert.True(comparison.Comparable);
    }

    /// Recall's tolerance is one point, which is just over half of what a
    /// single query is worth at 54 scored queries.
    [Fact]
    public void RecallFallingPastItsToleranceIsARegression()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(recall1: 0.74));

        Assert.False(comparison.Passed);
        Assert.Equal(ComparisonOutcome.Regressed, Metric(comparison, "recall@1").Outcome);
    }

    [Fact]
    public void RecallFallingWithinItsToleranceDoesNot()
    {
        Assert.True(BaselineComparison.Compare(Baseline(), Run(recall1: 0.755)).Passed);
    }

    /// Classification is the loose one on purpose: nothing downstream reads the
    /// category, so the gate is there to catch breakage rather than drift.
    [Fact]
    public void ClassificationDriftWellBeyondItsRunToRunRangeStillPasses()
    {
        // 4pp below baseline - far more than the 0.7pp four runs actually
        // moved, and still not the ten-point drop a mangled prompt produces.
        Assert.True(BaselineComparison.Compare(Baseline(), Run(accuracy: 0.88, macroF1: 0.89)).Passed);
    }

    [Fact]
    public void ClassificationCollapsingIsARegression()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(accuracy: 0.60, macroF1: 0.61));

        Assert.Equal(ComparisonOutcome.Regressed, Metric(comparison, "classification accuracy").Outcome);
        Assert.Equal(ComparisonOutcome.Regressed, Metric(comparison, "classification macro-F1").Outcome);
    }

    [Fact]
    public void CitationValidityFallingThreePointsIsARegression()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(citationValidity: 0.97));

        Assert.Equal(ComparisonOutcome.Regressed, Metric(comparison, "citation validity").Outcome);
    }

    /// The asymmetry, and the reason for it: a gate that fires when a number
    /// improves is one people route around.
    [Fact]
    public void ImprovingEveryMetricPasses()
    {
        var better = Run(recall1: 0.95, accuracy: 0.99, macroF1: 0.99, citationValidity: 1.0, costTotal: 0.01m);

        Assert.True(BaselineComparison.Compare(Baseline(), better).Passed);
    }

    /// Cost is the one metric where up is the bad direction.
    [Fact]
    public void CostRisingBeyondItsToleranceIsARegression()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(costTotal: 0.15m));

        Assert.Equal(ComparisonOutcome.Regressed, Metric(comparison, "cost total").Outcome);
    }

    [Fact]
    public void CostDriftingAFewPercentDoesNot()
    {
        Assert.True(BaselineComparison.Compare(Baseline(), Run(costTotal: 0.078m)).Passed);
    }

    /// A gate that silently covers less than it claims is the failure this
    /// whole phase exists to prevent, so a gated metric the run did not
    /// produce fails rather than being skipped.
    [Fact]
    public void AGatedMetricTheRunDidNotMeasureFails()
    {
        var comparison = BaselineComparison.Compare(
            Baseline(), Run(citationValidity: null, accuracy: null, macroF1: null));

        Assert.False(comparison.Passed);
        Assert.Equal(ComparisonOutcome.NotMeasured, Metric(comparison, "citation validity").Outcome);
        Assert.Equal(ComparisonOutcome.NotMeasured, Metric(comparison, "classification accuracy").Outcome);
    }

    /// The mirror case: a baseline written from a run that never measured
    /// something does not start gating it later.
    [Fact]
    public void AMetricTheBaselineDoesNotGateIsNotRequired()
    {
        var retrievalOnly = EvaluationBaseline.From(
            Run(accuracy: null, macroF1: null, citationValidity: null, costTotal: null));

        Assert.True(BaselineComparison.Compare(retrievalOnly, Run()).Passed);
    }

    /// Provenance mismatches are not regressions, and conflating them would
    /// report a deliberate prompt change as the system getting worse.
    [Fact]
    public void AChangedPromptMakesTheBaselineInapplicableRatherThanFailed()
    {
        var comparison = BaselineComparison.Compare(
            Baseline(), Run(classificationPrompt: "classify-v2"));

        Assert.False(comparison.Comparable);
        Assert.False(comparison.Passed);
        Assert.Contains(comparison.ProvenanceMismatches, m => m.Contains("classification prompt"));
        Assert.Contains("no longer applies", comparison.ToMarkdown());
    }

    [Fact]
    public void AChangedRedactionVersionIsAlsoAMismatch()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(redaction: "v2"));

        Assert.Contains(comparison.ProvenanceMismatches, m => m.Contains("redaction"));
    }

    /// Moving a routing floor changes NotEligible, which is citation validity's
    /// denominator - so a retune would read as a citation regression if the
    /// floors were not part of the provenance.
    [Fact]
    public void AMovedRoutingFloorIsAMismatch()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(gateA: 0.70));

        Assert.Contains(comparison.ProvenanceMismatches, m => m.Contains("Gate A floor"));
    }

    [Fact]
    public void TheMarkdownNamesTheRegressedMetric()
    {
        var markdown = BaselineComparison.Compare(Baseline(), Run(recall1: 0.70)).ToMarkdown();

        Assert.Contains("recall@1", markdown);
        Assert.Contains("REGRESSED", markdown);
        Assert.Contains("The gate failed", markdown);
    }

    /// The bug this rule exists for. A logic run has no chat deployment and
    /// generates nothing, but the prompt version was a compile-time constant
    /// stamped unconditionally, so the run claimed "draft-v2" and the gate
    /// refused to compare a recall baseline against it. The run now records
    /// null, and a baseline that gates no generation does not care.
    [Fact]
    public void ARunThatSkippedAStageTheBaselineDoesNotGate_IsStillComparable()
    {
        var baseline = EvaluationBaseline.From(Run(
            accuracy: null, macroF1: null, citationValidity: null, costTotal: null,
            classificationPrompt: null, gateA: null, generationPrompt: null, chatModel: null));

        var comparison = BaselineComparison.Compare(
            baseline,
            Run(accuracy: null, macroF1: null, citationValidity: null, costTotal: null,
                classificationPrompt: null, gateA: null, generationPrompt: null, chatModel: null));

        Assert.True(comparison.Comparable);
        Assert.Empty(comparison.ProvenanceMismatches);
    }

    /// Not symmetric with the case above, deliberately. A baseline that covers
    /// generation is not satisfied by a run that produced none - the numbers
    /// it gates were never computed, so "no regression" would be a claim about
    /// nothing.
    [Fact]
    public void ARunThatSkippedAStageTheBaselineDoesGate_IsNotComparable()
    {
        var comparison = BaselineComparison.Compare(
            Baseline(),
            Run(generationPrompt: null));

        Assert.False(comparison.Comparable);
        Assert.Contains(
            comparison.ProvenanceMismatches,
            m => m.Contains("generation prompt") && m.Contains("did not run that stage"));
    }

    /// Recorded in the baseline since the first one and never compared, so a
    /// 1.0 -> 1.1 price-list change passed a gate that existed to catch it.
    /// Cost tolerances mean nothing across two price lists.
    [Fact]
    public void AChangedPricingVersion_IsAProvenanceMismatch()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(pricingVersion: "1.1"));

        Assert.False(comparison.Comparable);
        Assert.Contains(comparison.ProvenanceMismatches, m => m.Contains("pricing version"));
    }

    /// A run that priced nothing against a baseline that did. Same rule as
    /// generation: the baseline gates cost, the run has none.
    [Fact]
    public void ARunWithNoPricing_AgainstABaselineWithPricing_IsNotComparable()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(costTotal: null));

        Assert.False(comparison.Comparable);
        Assert.Contains(
            comparison.ProvenanceMismatches,
            m => m.Contains("pricing version") && m.Contains("did not run that stage"));
    }

    [Fact]
    public void AChangedGateAFloor_IsStillAProvenanceMismatch()
    {
        var comparison = BaselineComparison.Compare(Baseline(), Run(gateA: 0.55));

        Assert.False(comparison.Comparable);
        Assert.Contains(comparison.ProvenanceMismatches, m => m.Contains("Gate A floor"));
    }

    /// Pricing gets the same treatment as the prompts. A logic run loads the
    /// price list and prices nothing, so it records no version and a recall
    /// baseline is not held hostage to a price change it never used.
    [Fact]
    public void ARunThatPricedNothing_RecordsNoPricingVersion()
    {
        Assert.Null(Run(costTotal: null).PricedPricingVersion);
        Assert.Equal("1.0", Run().PricedPricingVersion);
    }

    /// A baseline is a claim about *this dataset's* numbers. A held-out run
    /// swaps the queries for a file carrying no relevance labels, so its
    /// recall is a mean over nothing and its other figures are over a
    /// different population. Committing one would make the gate guard the
    /// wrong thing while looking exactly like it guards the right one.
    [Fact]
    public void ABaselineCannotBeWrittenFromAHeldOutRun()
    {
        var run = Run(holdout: "eval/dataset/judge-holdout.json");

        var real = Assert.Throws<InvalidOperationException>(run.EnsureCanBeBaseline);
        Assert.Contains("judge-holdout.json", real.Message);

        // The logic baseline is refused for the same reason. Narrowing one
        // refusal and forgetting the other is how a guard quietly stops
        // guarding half of what it covers.
        var logic = Assert.Throws<InvalidOperationException>(
            Run(holdout: "eval/dataset/judge-holdout.json", chatModel: null).EnsureCanBeLogicBaseline);
        Assert.Contains("judge-holdout.json", logic.Message);
    }

    [Fact]
    public void AnOrdinaryRunIsStillAllowedToBecomeABaseline()
    {
        Run().EnsureCanBeBaseline();
    }
}
