import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { SimilarTicket, TicketDraft } from '../../api/client'
import { DraftPanel } from './DraftPanel'

const DRAFT_TEXT = 'Both charges relate to a single order.'

function draft(overrides: Partial<TicketDraft> = {}): TicketDraft {
  return {
    citationsValid: true,
    draftText: DRAFT_TEXT,
    citations: [{ ticketId: 'source-1', rank: 1, similarity: 0.91 }],
    invalidCitationCount: 0,
    chatModel: 'gpt-4o-mini',
    promptVersion: 'v1',
    createdAt: '2026-10-06T10:55:48Z',
    ...overrides,
  }
}

const evidence: SimilarTicket[] = [
  {
    ticketId: 'source-1',
    subject: 'Duplicate charge',
    redactedText: 'redacted',
    resolution: 'The second charge was an authorisation hold.',
    similarity: 0.91,
  },
]

describe('DraftPanel', () => {
  it('shows the draft and names the ticket each citation points at', () => {
    render(<DraftPanel draft={draft()} evidence={evidence} draftEligible={true} />)

    expect(screen.getByText(DRAFT_TEXT)).toBeInTheDocument()
    expect(screen.getByText('Duplicate charge')).toBeInTheDocument()
    expect(screen.getByText('similarity 0.91')).toBeInTheDocument()
  })

  /// The guarantee from ADR-010, surfaced. The API already withholds the text
  /// and the citations; the UI must explain why rather than render an empty
  /// box or imply no draft was ever attempted.
  it('explains a withheld draft and never shows its text', () => {
    const withheld = draft({
      citationsValid: false,
      draftText: null,
      citations: [],
      invalidCitationCount: 2,
    })

    render(<DraftPanel draft={withheld} evidence={evidence} draftEligible={true} />)

    expect(screen.getByRole('alert')).toHaveTextContent('This draft is withheld')
    expect(screen.getByRole('alert')).toHaveTextContent('2 sources')
    expect(screen.queryByText(DRAFT_TEXT)).not.toBeInTheDocument()
  })

  it('distinguishes a gated ticket from one that simply has no draft yet', () => {
    const { unmount } = render(
      <DraftPanel draft={null} evidence={[]} draftEligible={false} />,
    )
    expect(screen.getByText(/routed for manual triage/i)).toBeInTheDocument()
    unmount()

    render(<DraftPanel draft={null} evidence={[]} draftEligible={true} />)
    expect(screen.getByText(/no draft has been generated yet/i)).toBeInTheDocument()
  })

  /// The draft records the ids it was shown; the evidence list is fetched
  /// live, so a cited ticket can legitimately have dropped out of retrieval.
  /// Falling back to the bare id is honest; silently omitting it would hide
  /// that the draft rests on something no longer there.
  it('falls back to the id when a cited ticket is no longer retrieved', () => {
    render(<DraftPanel draft={draft()} evidence={[]} draftEligible={true} />)

    expect(screen.getByText('source-1')).toBeInTheDocument()
    expect(screen.getByText(/no longer retrieved/)).toBeInTheDocument()
  })

  it('calls out a draft that cited nothing at all', () => {
    render(
      <DraftPanel draft={draft({ citations: [] })} evidence={evidence} draftEligible={true} />,
    )

    expect(screen.getByText(/nothing supports it/)).toBeInTheDocument()
  })
})
