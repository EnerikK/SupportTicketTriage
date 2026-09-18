using SupportTicketTriage.Eval.Metrics;

namespace SupportTicketTriage.UnitTests;

public class RetrievalMetricsTests
{
    private static RetrievalCase Case(string[] relevant, string[] retrieved) =>
        new("q", relevant, retrieved);

    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(2, 1.0)]
    [InlineData(5, 1.0)]
    public void RecallAt_FindsTheRelevantTicketOnlyOnceKReachesItsRank(int k, double expected)
    {
        var @case = Case(["b"], ["a", "b", "c"]);

        Assert.Equal(expected, RetrievalMetrics.RecallAt(@case, k));
    }

    [Fact]
    public void RecallAt_IsTheFractionOfRelevantTicketsFound()
    {
        // Two relevant, one of them inside the top 2.
        var @case = Case(["b", "z"], ["a", "b", "c"]);

        Assert.Equal(0.5, RetrievalMetrics.RecallAt(@case, 2));
    }

    [Fact]
    public void RecallAt_CountsDuplicateLabelsOnce()
    {
        var @case = Case(["b", "b"], ["a", "b"]);

        Assert.Equal(1.0, RetrievalMetrics.RecallAt(@case, 2));
    }

    [Fact]
    public void RecallAt_ReturnsNullWhenNothingIsLabelledRelevant()
    {
        // A malformed or injection query may have no correct answer. Scoring it
        // zero would report a failure that never happened.
        Assert.Null(RetrievalMetrics.RecallAt(Case([], ["a", "b"]), 5));
    }

    [Fact]
    public void RecallAt_IsZeroWhenNothingWasRetrieved()
    {
        Assert.Equal(0.0, RetrievalMetrics.RecallAt(Case(["a"], []), 5));
    }

    [Fact]
    public void RecallAt_RejectsANonPositiveK()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetrievalMetrics.RecallAt(Case(["a"], ["a"]), 0));
    }

    [Fact]
    public void MeanRecallAt_AveragesOnlyOverLabelledQueries()
    {
        RetrievalCase[] cases =
        [
            new("hit", ["a"], ["a"]),
            new("miss", ["b"], ["z"]),
            new("unlabelled", [], ["a", "b"]),
        ];

        // 1.0 and 0.0 average to 0.5; the unlabelled case must not drag it to 0.33.
        Assert.Equal(0.5, RetrievalMetrics.MeanRecallAt(cases, 5));
        Assert.Equal(2, RetrievalMetrics.ScoredCaseCount(cases));
        Assert.Equal(1, RetrievalMetrics.UnscoredCaseCount(cases));
    }

    [Fact]
    public void MeanRecallAt_IsZeroWhenNoQueryIsLabelled()
    {
        Assert.Equal(0, RetrievalMetrics.MeanRecallAt([new RetrievalCase("q", [], ["a"])], 5));
    }
}
