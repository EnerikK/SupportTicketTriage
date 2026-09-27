namespace SupportTicketTriage.Application.Routing;

/// The gates a ticket must clear to be eligible for a generated draft.
///
/// Two independent gates rather than one blended score, per ADR-003: they fail
/// for different reasons and an average would hide which. Gate B is the one
/// that is easy to forget — if nothing sufficiently similar was retrieved, a
/// *grounded* draft is impossible no matter how certain the classifier is.
[Flags]
public enum RoutingGate
{
    None = 0,

    /// Gate A — the classifier's self-reported score for its chosen category.
    ClassificationScore = 1,

    /// Gate B — retrieval sufficiency, measured as top-1 cosine similarity.
    RetrievalSimilarity = 2,
}

/// The floors each gate must clear.
///
/// These values are placeholders and ADR-003 says so explicitly. They are
/// chosen in Phase 5 by plotting accuracy and groundedness against score
/// buckets on the evaluation set. Until then they are configured, and no
/// claim is made about them anywhere — README included.
public sealed record RoutingThresholds(double MinimumClassificationScore, double MinimumSimilarity)
{
    public const double DefaultMinimumClassificationScore = 0.70;
    public const double DefaultMinimumSimilarity = 0.60;

    public static RoutingThresholds Default { get; } =
        new(DefaultMinimumClassificationScore, DefaultMinimumSimilarity);
}

/// What the system decided, and everything needed to reproduce that decision.
public sealed record RoutingDecision(
    bool IsDraftEligible,
    RoutingGate FailedGates,
    double? ClassificationScore,
    double? TopSimilarity,
    RoutingThresholds Thresholds)
{
    /// A null input is a failed gate, not an error.
    ///
    /// An unclassified ticket and a ticket the classifier was unsure about are
    /// both ineligible, but they are different operational problems, so the
    /// inputs stay nullable and the stored row distinguishes them.
    ///
    /// The floors are inclusive: a value exactly on the floor passes. A
    /// threshold that rejected the value it names would be a trap for whoever
    /// tunes these in Phase 5.
    public static RoutingDecision Evaluate(
        double? classificationScore,
        double? topSimilarity,
        RoutingThresholds thresholds)
    {
        var failed = RoutingGate.None;

        if (classificationScore is null || classificationScore < thresholds.MinimumClassificationScore)
        {
            failed |= RoutingGate.ClassificationScore;
        }

        if (topSimilarity is null || topSimilarity < thresholds.MinimumSimilarity)
        {
            failed |= RoutingGate.RetrievalSimilarity;
        }

        return new RoutingDecision(
            failed == RoutingGate.None,
            failed,
            classificationScore,
            topSimilarity,
            thresholds);
    }
}
