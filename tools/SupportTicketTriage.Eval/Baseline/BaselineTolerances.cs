namespace SupportTicketTriage.Eval.Baseline;

/// How far each metric may move before the gate calls it a regression.
///
/// **These are set by consequence, not uniformly by noise.** A regression in
/// recall degrades what a reviewer is asked to approve; a regression in
/// classification accuracy changes a label on a screen next to the ticket text
/// the reviewer can already read. Tuning both to the same sensitivity would
/// either fire constantly on the harmless one or let the harmful one through.
///
/// They live in the baseline file rather than in a separate one so that
/// "we loosened the gate" and "we moved the numbers" arrive in the same diff.
/// A tolerance reviewed apart from the figure it guards is a tolerance nobody
/// is really reviewing.
public sealed record BaselineTolerances(
    double RecallMaxDrop,
    double ClassificationAccuracyMaxDrop,
    double ClassificationMacroF1MaxDrop,
    double CitationValidityMaxDrop,
    double CostMaxRelativeIncrease)
{
    /// Recall has never moved: five runs against the same dataset and
    /// deployment produced bit-identical figures to sixteen decimal places.
    ///
    /// Not zero all the same, because the embedding stamp is a deployment name
    /// rather than an immutable model version - Azure can repoint it and move
    /// recall without a line of code changing. One point is the meaningful
    /// unit: at 54 scored queries a single query is worth 1.85pp, so this fires
    /// if any one query changes its result and not before.
    public const double DefaultRecallMaxDrop = 0.010;

    /// Classification is the least consequential metric here, and the tolerance
    /// says so.
    ///
    /// Nothing downstream reads the category. The generation prompt does not
    /// mention it, retrieval does not use it, and Gate A compares the
    /// self-reported *score* rather than the label. A wrong category changes
    /// what the queue displays beside a ticket whose text the reviewer is
    /// reading anyway.
    ///
    /// So the gate's job here is catching **breakage** - a mangled prompt, a
    /// swapped model, broken enum parsing - and those arrive as ten points or
    /// more, not one.
    ///
    /// Measured run-to-run range across five runs of the identical dataset is
    /// **1.67pp** for accuracy and **1.64pp** for macro-F1. Note that the first
    /// four runs suggested 0.7pp and 0.5pp, and the fifth more than doubled
    /// both - which is the argument against setting this from a handful of
    /// runs at all, and the reason it is set from what a regression costs
    /// instead. Five points absorbs the observed spread with room and still
    /// catches anything genuinely broken.
    public const double DefaultClassificationMaxDrop = 0.050;

    /// The safety metric, and tight because of what it means rather than how it
    /// moves. A draft citing evidence it was never shown is the failure
    /// [[ADR-010]]'s server-side validation exists to make impossible, so any
    /// real movement here is worth stopping for. It has read 100% on every run
    /// that measured it.
    public const double DefaultCitationValidityMaxDrop = 0.020;

    /// Deliberately loose. Two runs came in 0.05% apart, because token counts
    /// for fixed prompts are near-deterministic - so a tight bound here would
    /// fire on how verbose the model felt rather than on anything anyone did.
    /// The cost regression worth catching is a prompt that doubled in length.
    public const double DefaultCostMaxRelativeIncrease = 0.10;

    public static BaselineTolerances Default { get; } = new(
        DefaultRecallMaxDrop,
        DefaultClassificationMaxDrop,
        DefaultClassificationMaxDrop,
        DefaultCitationValidityMaxDrop,
        DefaultCostMaxRelativeIncrease);

    public void Validate()
    {
        var negative = new[]
        {
            (nameof(RecallMaxDrop), RecallMaxDrop),
            (nameof(ClassificationAccuracyMaxDrop), ClassificationAccuracyMaxDrop),
            (nameof(ClassificationMacroF1MaxDrop), ClassificationMacroF1MaxDrop),
            (nameof(CitationValidityMaxDrop), CitationValidityMaxDrop),
            (nameof(CostMaxRelativeIncrease), CostMaxRelativeIncrease),
        }.Where(t => t.Item2 < 0).Select(t => t.Item1).ToList();

        if (negative.Count > 0)
        {
            throw new InvalidOperationException(
                $"Negative tolerance(s): {string.Join(", ", negative)}. A negative tolerance would "
                + "fail a run for improving, which is not a gate anyone would keep.");
        }

        // A tolerance of 1.0 on a rate means "anything at all", which is a gate
        // switched off while looking switched on. The same mistake as zeroing
        // a routing floor, and caught the same way.
        var disabled = new[]
        {
            (nameof(RecallMaxDrop), RecallMaxDrop),
            (nameof(ClassificationAccuracyMaxDrop), ClassificationAccuracyMaxDrop),
            (nameof(ClassificationMacroF1MaxDrop), ClassificationMacroF1MaxDrop),
            (nameof(CitationValidityMaxDrop), CitationValidityMaxDrop),
        }.Where(t => t.Item2 >= 1.0).Select(t => t.Item1).ToList();

        if (disabled.Count > 0)
        {
            throw new InvalidOperationException(
                $"Tolerance(s) at or above 1.0: {string.Join(", ", disabled)}. A rate cannot drop "
                + "further than that, so the gate would pass anything while appearing to guard it.");
        }
    }
}
