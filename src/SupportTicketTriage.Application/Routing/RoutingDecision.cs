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
/// **These are no longer placeholders.** Both were selected from the
/// evaluation set on 2026-10-13 and ADR-019 records the tables. The two
/// gates came out of that exercise very differently, and the difference is
/// the point.
public sealed record RoutingThresholds(double MinimumClassificationScore, double MinimumSimilarity)
{
    /// Gate A is a tripwire, not a calibrated gate, and the measurement is
    /// what says so.
    ///
    /// The classifier's self-reported score has a separation of **0.006** —
    /// the mean score of a correct classification and of a wrong one are
    /// indistinguishable. 80% of tickets receive exactly 0.85. Accuracy sits
    /// at 94.2-94.4% for every floor up to 0.80, and the one ticket that
    /// scored 0.30 was classified correctly.
    ///
    /// So no floor on this score buys accuracy: raising it to 0.85 would gain
    /// 1.8 points while refusing 83% of tickets, 231 of which the classifier
    /// got right. The floor is therefore set low, to catch a response that is
    /// degenerate rather than merely uncertain, and nothing in this project
    /// claims this value discriminates. It mainly earns its keep through the
    /// null case: a ticket that was never classified has no score at all and
    /// fails this gate, which is what sends every ticket to a human when
    /// Azure OpenAI is unreachable.
    public const double DefaultMinimumClassificationScore = 0.50;

    /// Gate B is a real gate, and this is the lowest floor that does its job.
    ///
    /// Separation is **0.141**, twenty times Gate A's. The decisive evidence
    /// is the six malformed queries - tickets like "???" and "where parcel"
    /// that have no correct answer and must not receive a drafted reply.
    /// Their top-1 similarities were 0.258, 0.364, 0.531, 0.547, 0.550 and
    /// 0.598, so **0.60 is the lowest floor on the grid that refuses all six**.
    /// A floor of 0.55 lets two of them through.
    ///
    /// The cost is real and measured: 0.60 also gates 6 of 54 labelled
    /// queries that would have retrieved something relevant. Those go to a
    /// human instead of getting a draft, which is the right direction to err
    /// for a gate whose job is refusing to answer.
    ///
    /// The value is unchanged from the original guess. That is a result, not
    /// an absence of one - the guess happened to be right, and it is now the
    /// floor the evidence points at rather than the floor nobody had checked.
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
