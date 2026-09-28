namespace SupportTicketTriage.Infrastructure.Persistence;

/// One piece of evidence a draft was grounded on.
///
/// A child table rather than a list of ids on the draft, because rank and
/// similarity are the first two things anyone asks when a draft grounded
/// itself on the wrong precedent, and neither has a natural home in a bare
/// id list.
public sealed class TicketDraftSource
{
    public Guid Id { get; private init; }
    public Guid DraftId { get; private init; }

    /// The resolved ticket that was offered as evidence.
    public Guid SourceTicketId { get; private init; }

    /// Position in the retrieval result, starting at 1.
    public int Rank { get; private init; }

    public double Similarity { get; private init; }

    /// Whether the model actually cited this source. Retrieved-but-uncited is
    /// normal and worth knowing: a draft citing one of five sources is a
    /// different thing from one citing all five.
    public bool WasCited { get; private init; }

    private TicketDraftSource()
    {
    }

    internal static TicketDraftSource Create(
        Guid draftId,
        Guid sourceTicketId,
        int rank,
        double similarity,
        bool wasCited) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            DraftId = draftId,
            SourceTicketId = sourceTicketId,
            Rank = rank,
            Similarity = similarity,
            WasCited = wasCited,
        };
}
