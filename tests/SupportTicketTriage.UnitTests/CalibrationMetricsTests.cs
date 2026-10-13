using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class CalibrationMetricsTests
{
    private static CalibrationCase Case(double? score, bool succeeded) =>
        new(Guid.NewGuid().ToString(), score, succeeded);

    /// A score that separates perfectly: everything at 0.9 worked, everything
    /// at 0.1 did not. A floor between them should keep all the successes and
    /// gate only failures.
    private static List<CalibrationCase> PerfectlySeparated() =>
    [
        Case(0.9, true), Case(0.9, true), Case(0.9, true),
        Case(0.1, false), Case(0.1, false),
    ];

    [Fact]
    public void AFloorKeepsEverythingAtOrAboveIt()
    {
        // Inclusive, matching RoutingDecision.Evaluate. A calibration table
        // that disagreed with the gate it configures would be a trap.
        var outcome = Assert.Single(
            CalibrationMetrics.AtEachFloor([Case(0.6, true)], [0.6]));

        Assert.Equal(1, outcome.Passed);
        Assert.Equal(0, outcome.Gated);
    }

    [Fact]
    public void AWellChosenFloorPassesEverySuccessAndGatesEveryFailure()
    {
        var outcome = Assert.Single(
            CalibrationMetrics.AtEachFloor(PerfectlySeparated(), [0.5]));

        Assert.Equal(3, outcome.Passed);
        Assert.Equal(1.0, outcome.PassRate);
        Assert.Equal(2, outcome.Gated);
        Assert.Equal(0, outcome.GatedButWouldHaveSucceeded);
        Assert.Equal(0.0, outcome.GatedRate);
    }

    /// The cost side of the table. A floor set too high throws away work that
    /// would have succeeded, and that has to be visible or the decision is
    /// made on pass rate alone - which always improves as the floor rises.
    [Fact]
    public void AFloorSetTooHighReportsTheSuccessesItThrewAway()
    {
        var outcome = Assert.Single(
            CalibrationMetrics.AtEachFloor(PerfectlySeparated(), [0.95]));

        Assert.Equal(0, outcome.Passed);
        Assert.Equal(5, outcome.Gated);
        Assert.Equal(3, outcome.GatedButWouldHaveSucceeded);
        Assert.Equal(0.6, outcome.GatedRate!.Value, 10);
        Assert.Equal(1.0, outcome.GatedShare, 10);
    }

    [Fact]
    public void PassRateIsNullRatherThanZeroWhenAFloorPassesNothing()
    {
        var outcome = Assert.Single(CalibrationMetrics.AtEachFloor([Case(0.2, true)], [0.9]));

        Assert.Null(outcome.PassRate);
    }

    /// An absent score is not a zero score. Bucketing it at zero would invent
    /// a value the system never produced and drag the low floors' statistics
    /// around.
    [Fact]
    public void CasesWithNoScoreAreCountedApartRatherThanTreatedAsZero()
    {
        var cases = new[] { Case(0.8, true), Case(null, false), Case(null, false) };

        Assert.Equal(2, CalibrationMetrics.AbsentScoreCount(cases));

        var atZero = Assert.Single(CalibrationMetrics.AtEachFloor(cases, [0.0]));
        Assert.Equal(1, atZero.Passed);
        Assert.Equal(0, atZero.Gated);
    }

    /// The number that decides whether a gate on this score can work at all.
    [Fact]
    public void SeparationIsLargeWhenTheScorePredictsTheOutcome()
    {
        Assert.Equal(0.8, CalibrationMetrics.Separation(PerfectlySeparated())!.Value, 10);
    }

    /// The case this project actually hit: a near-constant score that says
    /// nothing about correctness. Separation near zero means no floor will
    /// help, which is a finding rather than a failure to find one.
    [Fact]
    public void SeparationIsNearZeroWhenTheScoreIsUninformative()
    {
        var uninformative = new[]
        {
            Case(0.85, true), Case(0.85, false), Case(0.85, true), Case(0.85, false),
        };

        Assert.Equal(0.0, CalibrationMetrics.Separation(uninformative)!.Value, 10);
    }

    [Fact]
    public void SeparationIsNullWhenEveryCaseWentTheSameWay()
    {
        Assert.Null(CalibrationMetrics.Separation([Case(0.9, true), Case(0.8, true)]));
    }

    [Fact]
    public void TheDefaultFloorsAreAFixedGridSoTwoRunsAreDiffable()
    {
        Assert.Equal(20, CalibrationMetrics.DefaultFloors.Count);
        Assert.Equal(0.0, CalibrationMetrics.DefaultFloors[0]);
        Assert.Equal(0.95, CalibrationMetrics.DefaultFloors[^1], 10);
    }
}
