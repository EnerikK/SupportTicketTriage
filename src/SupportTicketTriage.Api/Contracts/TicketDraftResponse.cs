using SupportTicketTriage.Infrastructure.Persistence;

namespace SupportTicketTriage.Api.Contracts;

public sealed record DraftCitationResponse(Guid TicketId, int Rank, double Similarity);

/// A draft as a reviewer is allowed to see it.
///
/// When citation validation failed, the text and the citations are withheld:
/// the specification requires that invalid citations never reach a reviewer
/// presented as trustworthy evidence, and a draft whose grounding cannot be
/// verified is not evidence of anything. The row still exists — the API just
/// does not serve it.
public sealed record TicketDraftResponse(
    bool CitationsValid,
    string? DraftText,
    IReadOnlyList<DraftCitationResponse> Citations,
    int InvalidCitationCount,
    string ChatModel,
    string PromptVersion,
    DateTimeOffset CreatedAt)
{
    public static TicketDraftResponse FromEntity(TicketDraft draft) =>
        new(
            draft.CitationsValid,
            draft.CitationsValid ? draft.DraftText : null,
            draft.CitationsValid
                ? draft.Sources
                    .Where(s => s.WasCited)
                    .OrderBy(s => s.Rank)
                    .Select(s => new DraftCitationResponse(s.SourceTicketId, s.Rank, s.Similarity))
                    .ToArray()
                : [],
            draft.InvalidCitationCount,
            draft.ChatModel,
            draft.PromptVersion,
            draft.CreatedAt);
}
