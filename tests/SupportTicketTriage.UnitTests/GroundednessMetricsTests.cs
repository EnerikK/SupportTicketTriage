using SupportTicketTriage.Eval.Judging;
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
}
