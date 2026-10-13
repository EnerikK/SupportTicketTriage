namespace SupportTicketTriage.Eval.Metrics;

/// One classified query: the label the dataset carries, and what the system
/// actually produced.
///
/// `Actual` is null when the ticket came back unclassified — an unparseable
/// response, an unknown category, an out-of-range score, or no response at
/// all. That is deliberately *not* an excluded case: see
/// <see cref="ClassificationMetrics"/>.
public sealed record ClassificationCase(string QueryId, string Expected, string? Actual)
{
    public bool IsCorrect => Actual is not null && Actual == Expected;
}

/// Precision, recall and F1 for one label, with the support it was computed
/// over. Support is carried because an F1 of 1.0 over two examples and an F1
/// of 1.0 over forty are not the same claim.
public sealed record LabelScore(string Label, double Precision, double Recall, double F1, int Support);

public sealed record ClassificationScore(
    double Accuracy,
    double MacroF1,
    int Total,
    int Unclassified,
    IReadOnlyList<LabelScore> PerLabel);

public static class ClassificationMetrics
{
    /// An unclassified ticket counts as wrong, not as absent.
    ///
    /// This differs from retrieval, where an unlabelled query is genuinely
    /// unscoreable and is excluded. Here the ground truth exists — the dataset
    /// says what the category is — so a model that returns unparseable JSON
    /// got it wrong. Excluding those would let a classifier that refuses half
    /// its inputs report perfect accuracy on the half it managed, which is the
    /// opposite of what this metric is for. The count is reported separately
    /// so the two failure modes stay distinguishable.
    public static double Accuracy(IReadOnlyCollection<ClassificationCase> cases) =>
        cases.Count == 0 ? 0 : (double)cases.Count(c => c.IsCorrect) / cases.Count;

    /// Macro-F1: the unweighted mean of per-label F1.
    ///
    /// Macro rather than micro because the dataset is not balanced across the
    /// six categories. Micro-F1 over a single-label problem collapses to
    /// accuracy, so it would hide a category the model never predicts behind
    /// the volume of the ones it does. Macro weights every category equally,
    /// which means one ignored category visibly drags the number down — that
    /// is the whole reason the specification asks for it.
    ///
    /// Labels with no support are excluded from the mean rather than scored
    /// zero. A category that never appears in the evaluation set had no
    /// opportunity to be predicted, and scoring it zero would report a failure
    /// that never had a chance to happen.
    public static double MacroF1(IReadOnlyCollection<ClassificationCase> cases)
    {
        var scored = PerLabel(cases).Where(s => s.Support > 0).ToList();

        return scored.Count == 0 ? 0 : scored.Average(s => s.F1);
    }

    /// Per-label precision, recall and F1, ordered by label so two runs are
    /// diffable.
    ///
    /// A label the model predicted but that never occurs in the dataset still
    /// appears here, with support 0 — those are false positives worth seeing,
    /// even though they are excluded from the macro mean.
    public static IReadOnlyList<LabelScore> PerLabel(IReadOnlyCollection<ClassificationCase> cases)
    {
        var labels = cases
            .Select(c => c.Expected)
            .Concat(cases.Select(c => c.Actual).OfType<string>())
            .Distinct()
            .OrderBy(label => label, StringComparer.Ordinal);

        return labels.Select(label => Score(cases, label)).ToList();
    }

    public static ClassificationScore Summarise(IReadOnlyCollection<ClassificationCase> cases) =>
        new(
            Accuracy(cases),
            MacroF1(cases),
            cases.Count,
            cases.Count(c => c.Actual is null),
            PerLabel(cases));

    private static LabelScore Score(IReadOnlyCollection<ClassificationCase> cases, string label)
    {
        var truePositives = cases.Count(c => c.Expected == label && c.Actual == label);
        var predicted = cases.Count(c => c.Actual == label);
        var support = cases.Count(c => c.Expected == label);

        // A label that was never predicted has undefined precision, and one
        // that never occurs has undefined recall. Both are reported as zero
        // rather than NaN: zero is what an F1 of a label the model never got
        // right should contribute, and NaN would poison the macro mean.
        var precision = predicted == 0 ? 0 : (double)truePositives / predicted;
        var recall = support == 0 ? 0 : (double)truePositives / support;
        var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

        return new LabelScore(label, precision, recall, f1, support);
    }
}
