import { toNumber, type SimilarTicket, type TicketDraft } from '../../api/client'

interface Props {
  draft: TicketDraft | null
  evidence: SimilarTicket[]
  draftEligible: boolean | null
}

/// Shown as the raw cosine similarity, not as a percentage. "91% similar"
/// reads as a calibrated claim about how alike two tickets are, and it is no
/// such thing - it is the cosine of the angle between two embeddings. The
/// same reason ADR-003 forbids dressing the routing signal up as confidence.
function similarityLabel(similarity: number | string): string {
  return `similarity ${toNumber(similarity).toFixed(2)}`
}

export function DraftPanel({ draft, evidence, draftEligible }: Props) {
  if (!draft) {
    return (
      <p className="status">
        {draftEligible === false
          ? 'No draft. This ticket was routed for manual triage.'
          : 'No draft has been generated yet.'}
      </p>
    )
  }

  // ADR-010: a draft that cited evidence it was never shown is withheld by the
  // API - draftText comes back null and the citations are empty. Saying only
  // "no draft" would hide a real generation failure, and rendering an empty
  // box would look like a bug. The reviewer is told what happened instead.
  if (!draft.citationsValid) {
    return (
      <div className="panel panel--withheld" role="alert">
        <p>
          <strong>This draft is withheld.</strong> It cited {draft.invalidCitationCount}{' '}
          {draft.invalidCitationCount === 1 ? 'source' : 'sources'} that were not in the
          evidence it was given, so neither its text nor its citations are shown.
        </p>
        <p className="muted">
          The draft is kept as a record that this happened. This ticket needs manual triage.
        </p>
      </div>
    )
  }

  const subjectOf = (ticketId: string) => evidence.find((e) => e.ticketId === ticketId)?.subject

  return (
    <div className="panel">
      <p className="draft__text">{draft.draftText}</p>

      <h3 className="subheading">Citations</h3>
      {draft.citations.length === 0 ? (
        <p className="muted">
          The draft cited none of the tickets it was given — nothing supports it.
        </p>
      ) : (
        <ul className="citations">
          {draft.citations.map((citation) => {
            const subject = subjectOf(citation.ticketId)

            return (
              <li key={citation.ticketId}>
                <span className="citations__rank">#{citation.rank}</span>{' '}
                {/* The draft stores the ids it was shown; the evidence list is
                    fetched live, so a cited ticket can legitimately be absent
                    from it if the corpus moved on since generation. */}
                {subject ?? <code>{citation.ticketId}</code>}{' '}
                <span className="muted">{similarityLabel(citation.similarity)}</span>
                {!subject && <span className="muted"> — no longer retrieved</span>}
              </li>
            )
          })}
        </ul>
      )}

      <p className="muted provenance">
        {draft.chatModel} · prompt {draft.promptVersion}
      </p>
    </div>
  )
}
