namespace SupportTicketTriage.Application.Generation;

/// The result of checking a draft's citations against the evidence it was
/// actually given.
///
/// Deterministic, with no model in the loop. This is the layer of ADR-002
/// that does not depend on anything behaving well: whatever the model emits,
/// a citation either names a ticket that was in that generation's retrieval
/// set or it does not, and that is decided here rather than by the model.
public sealed record CitationValidation(
    IReadOnlyList<Guid> Valid,
    IReadOnlyList<Guid> Invalid)
{
    public bool AllValid => Invalid.Count == 0;

    /// A cited id is valid only if it belongs to *this* generation's retrieval
    /// set. Existing as a ticket somewhere is not enough — a draft citing a
    /// real ticket it was never shown is still claiming support it does not
    /// have, and that is the failure mode this check exists for.
    public static CitationValidation Validate(
        IEnumerable<Guid> citedTicketIds,
        IReadOnlySet<Guid> retrievalSet)
    {
        var valid = new List<Guid>();
        var invalid = new List<Guid>();

        foreach (var id in citedTicketIds)
        {
            (retrievalSet.Contains(id) ? valid : invalid).Add(id);
        }

        return new CitationValidation(valid, invalid);
    }
}
