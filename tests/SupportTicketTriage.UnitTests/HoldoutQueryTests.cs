using SupportTicketTriage.Eval.Dataset;

namespace SupportTicketTriage.UnitTests;

/// The held-out query set: the dataset's corpus, somebody else's queries.
///
/// It exists because the 47 pairs that validated the judge are the same pairs
/// that produced its rubric, so they cannot also test it. Appending the
/// replacements to tickets.json would have bumped the dataset version,
/// invalidated both committed baselines, and changed recall@k - resetting the
/// one fully-established part of the evaluation in order to measure something
/// else. Hence a separate file ([[ADR-036]]).
public class HoldoutQueryTests
{
    private static EvalTicket Corpus(string id) =>
        new(id, TicketRole.Corpus, "Billing", "Low", $"Subject {id}", $"Body {id}")
        {
            Resolution = $"Resolution {id}",
        };

    private static EvalTicket Query(string id, params string[] relevant) =>
        new(id, TicketRole.Query, "Billing", "Low", $"Subject {id}", $"Body {id}")
        {
            RelevantIds = relevant,
        };

    private static EvalDataset MainSet() =>
        new("1.0", ["Billing"], [Corpus("c-1"), Corpus("c-2"), Query("q-1", "c-1")]);

    private static EvalDataset Holdout(params EvalTicket[] queries) =>
        new("1.0", ["Billing"], queries);

    [Fact]
    public void TheCorpusIsKeptAndTheQueriesAreReplaced()
    {
        var combined = MainSet().WithQueriesFrom(Holdout(Query("q-ho-1"), Query("q-ho-2")));

        Assert.Equal(["c-1", "c-2"], combined.Corpus.Select(t => t.Id));
        Assert.Equal(["q-ho-1", "q-ho-2"], combined.Queries.Select(t => t.Id));

        // The dataset's own query is gone rather than merged. A run over both
        // would mix queries the rubric was written from with queries it was
        // not, which is the exact contamination the held-out set avoids.
        Assert.DoesNotContain(combined.Tickets, t => t.Id == "q-1");
    }

    /// Validation runs over the combined set, not either half. A holdout id
    /// equal to a corpus id would otherwise seed two tickets sharing an id and
    /// retrieve whichever the database returned first.
    [Fact]
    public void AnIdCollidingWithTheCorpus_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => MainSet().WithQueriesFrom(Holdout(Query("c-2"))));

        Assert.Contains("Duplicate ticket ids", ex.Message);
    }

    /// The held-out file carries no relevance labels by design: it measures
    /// groundedness agreement, not retrieval. An empty RelevantIds must
    /// therefore load rather than fail.
    [Fact]
    public void QueriesWithoutRelevanceLabels_AreValid()
    {
        var combined = MainSet().WithQueriesFrom(Holdout(Query("q-ho-1")));

        Assert.All(combined.Queries, q => Assert.Empty(q.RelevantIds));
    }

    /// Still enforced across the join: a holdout query may not claim relevance
    /// to something the corpus does not contain.
    [Fact]
    public void AHoldoutQueryReferencingAnUnknownTicket_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => MainSet().WithQueriesFrom(Holdout(Query("q-ho-1", "c-missing"))));

        Assert.Contains("unknown ticket", ex.Message);
    }
}
