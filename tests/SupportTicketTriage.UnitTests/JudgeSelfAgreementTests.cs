using SupportTicketTriage.Eval.Judging;
using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class JudgeSelfAgreementTests
{
    private static IReadOnlyList<GroundednessCase> Run(params (string Id, bool? Grounded)[] verdicts) =>
        verdicts.Select(v => new GroundednessCase(v.Id, v.Grounded, null)).ToList();

    [Fact]
    public void AJudgeThatNeverChangesItsMind_IsFullyUnanimous()
    {
        var result = JudgeSelfAgreement.Compute(
        [
            Run(("a", true), ("b", false)),
            Run(("a", true), ("b", false)),
            Run(("a", true), ("b", false)),
        ]);

        Assert.Equal(1.0, result.UnanimousRate);
        Assert.Equal(1.0, result.MeanModalShare);
    }

    [Fact]
    public void AFlippedVerdict_IsReportedPerItem_AndInBothOverallFigures()
    {
        var result = JudgeSelfAgreement.Compute(
        [
            Run(("a", true), ("b", false)),
            Run(("a", true), ("b", true)),
            Run(("a", true), ("b", false)),
            Run(("a", true), ("b", false)),
        ]);

        var b = Assert.Single(result.Items, i => i.QueryId == "b");
        Assert.False(b.Unanimous);
        Assert.Equal(0.75, b.ModalShare);
        Assert.Equal([false, true, false, false], b.Verdicts);

        Assert.Equal(0.5, result.UnanimousRate);
        Assert.Equal((1.0 + 0.75) / 2, result.MeanModalShare);
    }

    /// Answering on one run and failing to on another is not agreement.
    [Fact]
    public void Unjudged_IsItsOwnOutcome_NotDropped()
    {
        var result = JudgeSelfAgreement.Compute(
        [
            Run(("a", true)),
            Run(("a", null)),
        ]);

        Assert.False(Assert.Single(result.Items).Unanimous);
    }

    /// A silent misalignment would report agreement between different drafts.
    [Fact]
    public void RunsOverDifferentDrafts_AreRefused() =>
        Assert.Throws<ArgumentException>(() => JudgeSelfAgreement.Compute(
        [
            Run(("a", true), ("b", true)),
            Run(("b", true), ("a", true)),
        ]));

    [Fact]
    public void OneRun_IsRefused() =>
        Assert.Throws<ArgumentException>(() => JudgeSelfAgreement.Compute([Run(("a", true))]));
}
