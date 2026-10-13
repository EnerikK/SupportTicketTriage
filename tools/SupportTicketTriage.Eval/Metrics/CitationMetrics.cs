namespace SupportTicketTriage.Eval.Metrics;

/// What happened when a draft was attempted for one query ticket.
///
/// The outcomes are kept apart because they mean different things. A ticket
/// the gates refused is the system working; a draft whose citations failed
/// validation is the model citing evidence it was never shown; a generation
/// failure is an unparseable or absent response. Collapsing them into one
/// "failed" bucket would hide which.
public enum DraftOutcomeKind
{
    /// A draft was produced and every cited id was in its retrieval set.
    Valid,

    /// A draft was produced and cited at least one id outside its retrieval
    /// set. Stored, never served.
    InvalidCitations,

    /// The routing gates sent the ticket to manual triage, or nothing was
    /// retrievable. No model call was made.
    NotEligible,

    /// The model was called and the response could not be used.
    Failed,
}

public sealed record CitationCase(string QueryId, DraftOutcomeKind Outcome, int CitedCount, int InvalidCount);

public sealed record CitationScore(
    double ValidityRate,
    int Attempted,
    int Valid,
    int InvalidCitations,
    int NotEligible,
    int Failed,
    int TotalCitations,
    int TotalInvalidCitations);

public static class CitationMetrics
{
    /// The fraction of *generated* drafts whose citations all validated.
    ///
    /// The denominator is drafts the model actually produced — Valid plus
    /// InvalidCitations — and deliberately excludes tickets the gates refused.
    /// Including them would make the metric improve every time the gates got
    /// stricter, which measures the gates rather than the model. Generation
    /// failures are excluded for the same reason: there were no citations to
    /// validate, so counting them would blend two different defects into one
    /// number. Both counts are reported separately so neither disappears.
    public static CitationScore Summarise(IReadOnlyCollection<CitationCase> cases)
    {
        var valid = cases.Count(c => c.Outcome == DraftOutcomeKind.Valid);
        var invalid = cases.Count(c => c.Outcome == DraftOutcomeKind.InvalidCitations);
        var attempted = valid + invalid;

        return new CitationScore(
            ValidityRate: attempted == 0 ? 0 : (double)valid / attempted,
            Attempted: attempted,
            Valid: valid,
            InvalidCitations: invalid,
            NotEligible: cases.Count(c => c.Outcome == DraftOutcomeKind.NotEligible),
            Failed: cases.Count(c => c.Outcome == DraftOutcomeKind.Failed),
            TotalCitations: cases.Sum(c => c.CitedCount),
            TotalInvalidCitations: cases.Sum(c => c.InvalidCount));
    }
}
