using SupportTicketTriage.Application.Generation;

namespace SupportTicketTriage.Infrastructure.Persistence;

/// A generated draft reply, the evidence it was given, and the outcome of
/// validating its citations against that evidence.
///
/// A draft that failed citation validation is still stored. It is never
/// served to a reviewer — the API withholds its text and its citations — but
/// discarding it would erase the only record that the model cited something
/// it was never shown, which is exactly the signal worth keeping and the
/// metric Phase 5 reports.
public sealed class TicketDraft
{
    private readonly List<TicketDraftSource> _sources = [];

    public Guid Id { get; private init; }
    public Guid TicketId { get; private init; }

    public string DraftText { get; private init; } = null!;

    /// False when the model cited anything outside its own retrieval set.
    public bool CitationsValid { get; private init; }

    /// How many cited ids were not in the retrieval set. The ids themselves
    /// are not kept: an invented id has no diagnostic value beyond the fact
    /// that it was invented, and the sources below already record which real
    /// ones were cited.
    public int InvalidCitationCount { get; private init; }

    public string ChatModel { get; private init; } = null!;
    public string PromptVersion { get; private init; } = null!;
    public string RedactionVersion { get; private init; } = null!;
    public DateTimeOffset CreatedAt { get; private init; }

    public IReadOnlyCollection<TicketDraftSource> Sources => _sources;

    private TicketDraft()
    {
    }

    public static TicketDraft Create(
        Guid ticketId,
        string draftText,
        IReadOnlyList<RetrievedSource> retrievalSet,
        CitationValidation citations,
        string chatModel,
        string promptVersion,
        string redactionVersion)
    {
        var draft = new TicketDraft
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticketId,
            DraftText = draftText,
            CitationsValid = citations.AllValid,
            InvalidCitationCount = citations.Invalid.Count,
            ChatModel = chatModel,
            PromptVersion = promptVersion,
            RedactionVersion = redactionVersion,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var cited = citations.Valid.ToHashSet();

        foreach (var source in retrievalSet)
        {
            draft._sources.Add(TicketDraftSource.Create(
                draft.Id,
                source.TicketId,
                source.Rank,
                source.Similarity,
                wasCited: cited.Contains(source.TicketId)));
        }

        return draft;
    }
}

/// One ticket that was offered to the model as grounding, in the order it was
/// offered.
public readonly record struct RetrievedSource(Guid TicketId, int Rank, double Similarity);
