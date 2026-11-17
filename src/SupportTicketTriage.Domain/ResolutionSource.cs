namespace SupportTicketTriage.Domain;

/// Where a ticket's resolution came from.
///
/// Numbered from 1 so that an unset value is never a valid source, the same
/// convention as ReviewDecision.
public enum ResolutionSource
{
    /// Written into the corpus directly, with no review. The evaluation
    /// harness seeds its corpus tickets this way.
    Imported = 1,

    /// A reviewer approved a draft, as written or edited. The text was
    /// written from precedents the draft cited and a human accepted it.
    ApprovedDraft = 2,
}
