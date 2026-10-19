using SupportTicketTriage.Eval.Instrumentation;

namespace SupportTicketTriage.Eval.Metrics;

/// What one stage cost in time.
///
/// `Failures` is reported beside the percentiles rather than folded into them.
/// A call that timed out spent real wall-clock time, but including it would
/// make the system look slow for a reason that is not slowness - and dropping
/// it silently would hide that the call happened. So failures are counted here
/// and excluded from P50/P95, which describe the latency of calls that worked.
public sealed record StageLatency(
    AiStage Stage,
    int Calls,
    int Failures,
    TimeSpan? P50,
    TimeSpan? P95,
    TimeSpan TotalElapsed);

public static class LatencyMetrics
{
    /// The nearest-rank percentile: sort, then take the value at
    /// `ceil(p / 100 * n)`, 1-based.
    ///
    /// **No interpolation, deliberately.** The interpolating definitions
    /// (linear, Excel's `PERCENTILE`, and the seven other textbook variants)
    /// return a number that no call actually took. For a latency figure that
    /// will be quoted in a README and compared between runs, a real observed
    /// measurement is easier to defend than a weighted average of two of them -
    /// "the 45th slowest of 47 calls" is a sentence; "the 95th percentile under
    /// linear interpolation" invites the question of which definition.
    ///
    /// The cost of that choice is granularity: with 47 generation calls, P95 is
    /// the 45th value and there is no P99 worth reporting at all. That is a
    /// property of the sample size rather than of the method, and the report
    /// states the call count next to the percentile so it cannot be read as
    /// more precise than it is.
    ///
    /// Returns null for an empty sample rather than zero - the harness-wide
    /// rule that an unmeasurable quantity is absent, not nothing.
    public static TimeSpan? Percentile(IReadOnlyCollection<TimeSpan> samples, int percentile)
    {
        if (percentile is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentile), percentile, "A percentile must be between 1 and 100.");
        }

        if (samples.Count == 0)
        {
            return null;
        }

        var ordered = samples.Order().ToList();

        // Math.Ceiling on doubles, then clamped: at percentile 100 the rank is
        // exactly n and the clamp is a no-op, but floating-point ceiling on a
        // value that should land on a whole number is the same hazard the
        // calibration floors hit, so the index is bounded rather than trusted.
        var rank = (int)Math.Ceiling(percentile / 100.0 * ordered.Count);

        return ordered[Math.Clamp(rank, 1, ordered.Count) - 1];
    }

    /// One row per stage that was actually exercised.
    ///
    /// Stages with no calls are omitted rather than reported as zero: a run
    /// with `--skip-generation` genuinely has no generation latency, and a row
    /// of zeroes would read as instantaneous rather than as absent.
    public static IReadOnlyList<StageLatency> Summarise(IReadOnlyCollection<AiCall> calls) =>
        calls
            .GroupBy(c => c.Stage)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var succeeded = g.Where(c => c.Succeeded).Select(c => c.Elapsed).ToList();

                return new StageLatency(
                    Stage: g.Key,
                    Calls: g.Count(),
                    Failures: g.Count(c => !c.Succeeded),
                    P50: Percentile(succeeded, 50),
                    P95: Percentile(succeeded, 95),
                    // Every call, failures included: this is what the run spent,
                    // and a failed call's time was still spent.
                    TotalElapsed: new TimeSpan(g.Sum(c => c.Elapsed.Ticks)));
            })
            .ToList();
}
