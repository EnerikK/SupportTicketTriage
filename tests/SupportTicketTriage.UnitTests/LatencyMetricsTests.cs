using SupportTicketTriage.Eval.Instrumentation;
using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class LatencyMetricsTests
{
    private static TimeSpan Ms(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    private static AiCall Call(AiStage stage, int milliseconds, bool succeeded = true) =>
        new(stage, Ms(milliseconds), InputTokens: 1, OutputTokens: 1, succeeded);

    /// Nearest-rank, so the answer is always a value that was actually
    /// observed. With four samples the 50th percentile is the 2nd value and the
    /// 95th is the 4th - an interpolating definition would return 2.5 and 3.85,
    /// neither of which any call took.
    [Fact]
    public void ThePercentileIsAnObservedValueRatherThanAnInterpolatedOne()
    {
        TimeSpan[] samples = [Ms(1), Ms(2), Ms(3), Ms(4)];

        Assert.Equal(Ms(2), LatencyMetrics.Percentile(samples, 50));
        Assert.Equal(Ms(4), LatencyMetrics.Percentile(samples, 95));
    }

    [Fact]
    public void ThePercentileSortsBeforeRanking()
    {
        TimeSpan[] unordered = [Ms(400), Ms(100), Ms(300), Ms(200)];

        Assert.Equal(Ms(200), LatencyMetrics.Percentile(unordered, 50));
    }

    [Fact]
    public void ASingleSampleIsItsOwnMedianAndItsOwnP95()
    {
        TimeSpan[] one = [Ms(42)];

        Assert.Equal(Ms(42), LatencyMetrics.Percentile(one, 50));
        Assert.Equal(Ms(42), LatencyMetrics.Percentile(one, 95));
    }

    /// The boundary the ceiling makes easy to get wrong: rank 100% must be the
    /// last element, not one past it.
    [Fact]
    public void TheHundredthPercentileIsTheSlowestCallAndDoesNotRunOffTheEnd()
    {
        TimeSpan[] samples = [Ms(1), Ms(2), Ms(3)];

        Assert.Equal(Ms(3), LatencyMetrics.Percentile(samples, 100));
    }

    [Fact]
    public void AnEmptySampleHasNoPercentileRatherThanAZeroOne()
    {
        Assert.Null(LatencyMetrics.Percentile([], 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void APercentileOutsideOneToAHundredIsRejected(int percentile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LatencyMetrics.Percentile([Ms(1)], percentile));
    }

    /// A failed call spent real time, so it counts toward the stage total and
    /// toward the failure count - but it must not drag the percentiles, which
    /// describe how long a working call takes.
    [Fact]
    public void FailuresAreCountedAndTotalledButExcludedFromThePercentiles()
    {
        AiCall[] calls =
        [
            Call(AiStage.Generation, 100),
            Call(AiStage.Generation, 200),
            Call(AiStage.Generation, 30_000, succeeded: false),
        ];

        var stage = Assert.Single(LatencyMetrics.Summarise(calls));

        Assert.Equal(3, stage.Calls);
        Assert.Equal(1, stage.Failures);
        Assert.Equal(Ms(100), stage.P50);
        Assert.Equal(Ms(200), stage.P95);
        Assert.Equal(Ms(30_300), stage.TotalElapsed);
    }

    [Fact]
    public void AStageWhoseCallsAllFailedHasNoPercentilesAtAll()
    {
        var stage = Assert.Single(
            LatencyMetrics.Summarise([Call(AiStage.Embedding, 500, succeeded: false)]));

        Assert.Equal(1, stage.Failures);
        Assert.Null(stage.P50);
        Assert.Null(stage.P95);
        Assert.Equal(Ms(500), stage.TotalElapsed);
    }

    /// A skipped stage is absent, not instantaneous. `--skip-generation` would
    /// otherwise report generation at 0ms, which reads as "very fast".
    [Fact]
    public void StagesThatNeverRanAreOmittedRatherThanReportedAsZero()
    {
        var stages = LatencyMetrics.Summarise(
            [Call(AiStage.Embedding, 10), Call(AiStage.Classification, 20)]);

        Assert.Equal(
            [AiStage.Embedding, AiStage.Classification],
            stages.Select(s => s.Stage));
    }
}
