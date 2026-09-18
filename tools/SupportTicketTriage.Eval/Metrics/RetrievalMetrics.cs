namespace SupportTicketTriage.Eval.Metrics;

/// One evaluated query: what should have been retrieved, and what was, in rank
/// order.
public sealed record RetrievalCase(
    string QueryId,
    IReadOnlyList<string> RelevantIds,
    IReadOnlyList<string> RetrievedIds);

public static class RetrievalMetrics
{
    /// Fraction of the relevant tickets that appear in the top k.
    ///
    /// Returns null when a query has no relevant tickets labelled. Such queries
    /// are legitimate - a malformed or injection case may have no correct
    /// answer - but scoring them as 0 would report a retrieval failure that
    /// never happened, and scoring them as 1 would flatter the result.
    public static double? RecallAt(RetrievalCase @case, int k)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(k, 1);

        if (@case.RelevantIds.Count == 0)
        {
            return null;
        }

        var topK = @case.RetrievedIds.Take(k).ToHashSet();
        var found = @case.RelevantIds.Distinct().Count(topK.Contains);

        return (double)found / @case.RelevantIds.Distinct().Count();
    }

    /// Mean recall across every query that carries at least one label.
    public static double MeanRecallAt(IEnumerable<RetrievalCase> cases, int k)
    {
        var scored = cases.Select(c => RecallAt(c, k)).OfType<double>().ToList();

        return scored.Count == 0 ? 0 : scored.Average();
    }

    public static int ScoredCaseCount(IEnumerable<RetrievalCase> cases) =>
        cases.Count(c => c.RelevantIds.Count > 0);

    public static int UnscoredCaseCount(IEnumerable<RetrievalCase> cases) =>
        cases.Count(c => c.RelevantIds.Count == 0);
}
