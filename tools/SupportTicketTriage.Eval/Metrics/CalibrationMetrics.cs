namespace SupportTicketTriage.Eval.Metrics;

/// One observation for calibrating a routing gate.
///
/// `Score` is the value the gate would compare against its floor, and
/// `Succeeded` is whether the thing the gate exists to protect actually
/// worked out. Both gates fit this shape: for Gate A the score is the
/// classifier's self-report and success is "classified correctly"; for Gate B
/// the score is top-1 retrieval similarity and success is "a relevant ticket
/// was actually retrieved".
///
/// A null score means the input was absent — never classified, or nothing
/// retrieved at all. Those always fail the gate regardless of where the floor
/// sits, so they are counted apart rather than bucketed at zero, which would
/// invent a score the system never produced.
public sealed record CalibrationCase(string Id, double? Score, bool Succeeded);

/// What a floor at <see cref="Floor"/> would do.
///
/// This is the shape a threshold decision actually needs: not "how accurate
/// is each bucket" but "if I set the floor here, what do I keep, what do I
/// throw away, and was the stuff I threw away actually bad".
public sealed record FloorOutcome(
    double Floor,
    int Passed,
    int PassedAndSucceeded,
    int Gated,
    int GatedButWouldHaveSucceeded)
{
    /// Success rate among the tickets this floor lets through. The number the
    /// gate is trying to protect.
    public double? PassRate => Passed == 0 ? null : (double)PassedAndSucceeded / Passed;

    /// Success rate among the tickets this floor blocks. **The cost of the
    /// gate**: every one of these would have worked and was refused anyway.
    public double? GatedRate => Gated == 0 ? null : (double)GatedButWouldHaveSucceeded / Gated;

    /// Share of all observations this floor would refuse. A floor that gates
    /// most of the corpus is not an operating point however good its pass
    /// rate looks.
    public double GatedShare => Passed + Gated == 0 ? 0 : (double)Gated / (Passed + Gated);
}

public static class CalibrationMetrics
{
    /// Candidate floors from 0.00 to 0.95 in steps of 0.05.
    ///
    /// Fixed steps rather than the observed score values, so two runs produce
    /// the same rows and can be diffed. An observed-value grid would move
    /// every time the model's output moved, which is the opposite of what a
    /// calibration table is for.
    ///
    /// **The rounding is load-bearing, and it was missing.** `i * 0.05` is not
    /// the decimal it looks like for seven of these twenty values: it yields
    /// 0.6000000000000001 where it prints 0.60, and 0.8500000000000001 where
    /// it prints 0.85. Since the floors are inclusive, a score of exactly 0.85
    /// was then *gated* by this table while the real gate *passes* it — the
    /// precise trap the comment below warns about. It mattered because the
    /// classifier emits exactly 0.85 for 80% of tickets, so the one row
    /// ADR-019 quotes was the one the error fell on. Rounding to the two
    /// decimals the grid claims makes each floor the value it reports.
    public static IReadOnlyList<double> DefaultFloors { get; } =
        Enumerable.Range(0, 20).Select(i => Math.Round(i * 0.05, 2)).ToList();

    /// The floors are inclusive, matching RoutingDecision.Evaluate — a value
    /// exactly on the floor passes. A table that disagreed with the gate it
    /// is used to configure would be a trap for whoever reads it.
    public static IReadOnlyList<FloorOutcome> AtEachFloor(
        IReadOnlyCollection<CalibrationCase> cases,
        IReadOnlyList<double>? floors = null)
    {
        var scored = cases.Where(c => c.Score is not null).ToList();

        return (floors ?? DefaultFloors)
            .Select(floor =>
            {
                var passed = scored.Where(c => c.Score >= floor).ToList();
                var gated = scored.Where(c => c.Score < floor).ToList();

                return new FloorOutcome(
                    floor,
                    passed.Count,
                    passed.Count(c => c.Succeeded),
                    gated.Count,
                    gated.Count(c => c.Succeeded));
            })
            .ToList();
    }

    /// Observations with no score at all. These fail every floor, so they are
    /// not evidence about where the floor belongs - but a gate that only ever
    /// fires on absent inputs is worth knowing about, because it means the
    /// score itself is doing nothing.
    public static int AbsentScoreCount(IReadOnlyCollection<CalibrationCase> cases) =>
        cases.Count(c => c.Score is null);

    /// How much the score actually separates success from failure: the mean
    /// score of the cases that succeeded minus the mean of those that did not.
    ///
    /// This is the single number that says whether a gate on this score can
    /// work at all. Near zero means the score carries no information about
    /// the outcome and **no floor will help** — which is a finding, not a
    /// failure to find one. Null when either group is empty.
    public static double? Separation(IReadOnlyCollection<CalibrationCase> cases)
    {
        var scored = cases.Where(c => c.Score is not null).ToList();
        var succeeded = scored.Where(c => c.Succeeded).Select(c => c.Score!.Value).ToList();
        var failed = scored.Where(c => !c.Succeeded).Select(c => c.Score!.Value).ToList();

        return succeeded.Count == 0 || failed.Count == 0
            ? null
            : succeeded.Average() - failed.Average();
    }
}
