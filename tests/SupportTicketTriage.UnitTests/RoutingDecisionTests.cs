using SupportTicketTriage.Application.Routing;

namespace SupportTicketTriage.UnitTests;

public class RoutingDecisionTests
{
    private static readonly RoutingThresholds Thresholds = new(0.70, 0.60);

    [Fact]
    public void BothGatesClear_IsDraftEligible()
    {
        var decision = RoutingDecision.Evaluate(0.85, 0.72, Thresholds);

        Assert.True(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.None, decision.FailedGates);
    }

    [Fact]
    public void LowClassificationScore_FailsGateAOnly()
    {
        var decision = RoutingDecision.Evaluate(0.51, 0.90, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.ClassificationScore, decision.FailedGates);
    }

    /// The gate that is easy to forget. A confident classifier means nothing
    /// if nothing similar was retrieved: a grounded draft is impossible.
    [Fact]
    public void LowSimilarity_FailsGateBOnly()
    {
        var decision = RoutingDecision.Evaluate(0.99, 0.10, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.RetrievalSimilarity, decision.FailedGates);
    }

    [Fact]
    public void BothBelowTheirFloors_FailBothGatesIndependently()
    {
        var decision = RoutingDecision.Evaluate(0.10, 0.10, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.ClassificationScore));
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.RetrievalSimilarity));
    }

    /// Absent and low both fail, but the stored inputs keep them apart: one is
    /// a model that declined to answer, the other a model that was unsure.
    [Fact]
    public void MissingClassification_FailsGateAAndIsRecordedAsAbsent()
    {
        var decision = RoutingDecision.Evaluate(null, 0.90, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.ClassificationScore, decision.FailedGates);
        Assert.Null(decision.ClassificationScore);
    }

    [Fact]
    public void MissingRetrieval_FailsGateBAndIsRecordedAsAbsent()
    {
        var decision = RoutingDecision.Evaluate(0.90, null, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.Equal(RoutingGate.RetrievalSimilarity, decision.FailedGates);
        Assert.Null(decision.TopSimilarity);
    }

    [Fact]
    public void NeitherInputPresent_FailsBothGates()
    {
        var decision = RoutingDecision.Evaluate(null, null, Thresholds);

        Assert.False(decision.IsDraftEligible);
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.ClassificationScore));
        Assert.True(decision.FailedGates.HasFlag(RoutingGate.RetrievalSimilarity));
    }

    /// The floors are inclusive. A threshold that rejected the value it names
    /// would be a trap for whoever tunes these against evaluation data.
    [Fact]
    public void ValuesExactlyOnTheFloor_Pass()
    {
        var decision = RoutingDecision.Evaluate(0.70, 0.60, Thresholds);

        Assert.True(decision.IsDraftEligible);
    }

    [Theory]
    [InlineData(0.6999)]
    [InlineData(0.0)]
    public void ValuesJustBelowTheFloor_Fail(double score)
    {
        var decision = RoutingDecision.Evaluate(score, 0.90, Thresholds);

        Assert.False(decision.IsDraftEligible);
    }

    /// Carried on the decision so the stored row can reproduce it once the
    /// configured floors move, which Phase 5 will do.
    [Fact]
    public void TheThresholdsUsed_TravelWithTheDecision()
    {
        var decision = RoutingDecision.Evaluate(0.85, 0.72, Thresholds);

        Assert.Equal(0.70, decision.Thresholds.MinimumClassificationScore);
        Assert.Equal(0.60, decision.Thresholds.MinimumSimilarity);
    }

    [Fact]
    public void Defaults_ArePlaceholdersButStillGateSomething()
    {
        // Guards against someone "removing the guess" by zeroing the floors,
        // which would make both gates unconditionally pass and quietly turn
        // the routing signal off.
        Assert.True(RoutingThresholds.Default.MinimumClassificationScore > 0);
        Assert.True(RoutingThresholds.Default.MinimumSimilarity > 0);
    }
}
