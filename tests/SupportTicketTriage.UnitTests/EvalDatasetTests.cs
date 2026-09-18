using SupportTicketTriage.Eval.Dataset;

namespace SupportTicketTriage.UnitTests;

public class EvalDatasetTests
{
    private static EvalTicket Corpus(string id, string? resolution = "resolved") =>
        new(id, TicketRole.Corpus, "Billing", "Normal", $"subject {id}", $"body {id}")
        {
            Resolution = resolution,
        };

    private static EvalTicket Query(string id, params string[] relevant) =>
        new(id, TicketRole.Query, "Billing", "Normal", $"subject {id}", $"body {id}")
        {
            RelevantIds = relevant,
        };

    private static EvalDataset Dataset(params EvalTicket[] tickets) =>
        new("test", ["Billing"], tickets);

    [Fact]
    public void Validate_AcceptsAConsistentDataset()
    {
        Dataset(Corpus("c1"), Query("q1", "c1")).Validate();
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Dataset(Corpus("c1"), Corpus("c1")).Validate());

        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void Validate_RejectsALabelPointingAtAnUnknownTicket()
    {
        // Silently tolerating this would depress recall and read as a retrieval
        // problem rather than a dataset problem.
        var ex = Assert.Throws<InvalidOperationException>(() => Dataset(Corpus("c1"), Query("q1", "nope")).Validate());

        Assert.Contains("unknown ticket", ex.Message);
    }

    [Fact]
    public void Validate_RejectsALabelPointingAtAnotherQuery()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Dataset(Corpus("c1"), Query("q1"), Query("q2", "q1")).Validate());

        Assert.Contains("not a corpus ticket", ex.Message);
    }

    [Fact]
    public void Validate_RejectsACorpusTicketWithNoResolution()
    {
        // Unresolved tickets are never corpus-eligible, so it could not be
        // retrieved no matter how good retrieval was.
        var ex = Assert.Throws<InvalidOperationException>(
            () => Dataset(Corpus("c1", resolution: null)).Validate());

        Assert.Contains("no resolution", ex.Message);
    }

    [Fact]
    public void Validate_AllowsAQueryWithNoLabels()
    {
        Dataset(Corpus("c1"), Query("malformed")).Validate();
    }

    [Fact]
    public void CorpusAndQueries_SplitByRole()
    {
        var dataset = Dataset(Corpus("c1"), Corpus("c2"), Query("q1", "c1"));

        Assert.Equal(2, dataset.Corpus.Count());
        Assert.Single(dataset.Queries);
    }
}
