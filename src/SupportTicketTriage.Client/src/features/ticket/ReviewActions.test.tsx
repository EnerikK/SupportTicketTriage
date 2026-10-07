import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TicketDraft, TicketReview } from '../../api/client'
import { ReviewActions } from './ReviewActions'

const TICKET_ID = '01a110da-f73f-7e2f-b69f-53d2ca38bc34'
const DRAFT_TEXT = 'Both charges relate to a single order.'

function draft(overrides: Partial<TicketDraft> = {}): TicketDraft {
  return {
    citationsValid: true,
    draftText: DRAFT_TEXT,
    citations: [],
    invalidCitationCount: 0,
    chatModel: 'gpt-4o-mini',
    promptVersion: 'v1',
    createdAt: '2026-10-06T10:55:48Z',
    ...overrides,
  }
}

function renderActions(props: Partial<Parameters<typeof ReviewActions>[0]> = {}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )

  return render(
    <ReviewActions
      ticketId={TICKET_ID}
      draft={draft()}
      review={null}
      draftEligible={true}
      {...props}
    />,
    { wrapper },
  )
}

function capturePost() {
  const calls: { url: string; body: unknown }[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url, body: init?.body ? JSON.parse(init.body as string) : undefined })

      return Promise.resolve({
        ok: true,
        status: 201,
        statusText: '',
        json: async () => ({}),
        text: async () => '{}',
      })
    }),
  )

  return calls
}

describe('ReviewActions', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  /// Approving as written sends no text and lets the server store the
  /// draft's own, so the client never asserts what the draft said.
  it('approves as written without sending any text back', async () => {
    const calls = capturePost()
    const user = userEvent.setup()

    renderActions()
    await user.click(screen.getByRole('button', { name: 'Approve as written' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].url).toContain(`/tickets/${TICKET_ID}/review`)
    expect(calls[0].body).toEqual({
      decision: 'Approved',
      finalText: null,
      rejectionReason: null,
    })
  })

  it('sends the reviewer text once the draft has been edited', async () => {
    const calls = capturePost()
    const user = userEvent.setup()

    renderActions()
    const textarea = screen.getByLabelText(/^Reply/)
    await user.clear(textarea)
    await user.type(textarea, 'We have released the hold.')

    // The label itself reports that this is now the edit-and-approve action.
    await user.click(screen.getByRole('button', { name: 'Approve edited reply' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].body).toEqual({
      decision: 'Approved',
      finalText: 'We have released the hold.',
      rejectionReason: null,
    })
  })

  /// Whitespace-only differences are not edits, which is the same rule the
  /// server applies when it records whether the reviewer changed anything.
  it('does not treat surrounding whitespace as an edit', async () => {
    const calls = capturePost()
    const user = userEvent.setup()

    renderActions()
    await user.type(screen.getByLabelText(/^Reply/), '   ')

    await user.click(screen.getByRole('button', { name: 'Approve as written' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].body).toMatchObject({ finalText: null })
  })

  it('will not reject without a reason', async () => {
    const user = userEvent.setup()
    renderActions()

    expect(screen.getByRole('button', { name: 'Reject' })).toBeDisabled()

    await user.type(screen.getByLabelText(/^Reason/), 'Cites the wrong precedent.')
    expect(screen.getByRole('button', { name: 'Reject' })).toBeEnabled()
  })

  it('sends the reason when rejecting', async () => {
    const calls = capturePost()
    const user = userEvent.setup()

    renderActions()
    await user.type(screen.getByLabelText(/^Reason/), 'Cites the wrong precedent.')
    await user.click(screen.getByRole('button', { name: 'Reject' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].body).toEqual({
      decision: 'Rejected',
      finalText: null,
      rejectionReason: 'Cites the wrong precedent.',
    })
  })

  it('shows a recorded decision instead of the form, with no way to change it', () => {
    const review: TicketReview = {
      decision: 'Approved',
      finalText: 'The agreed reply.',
      wasEdited: true,
      rejectionReason: null,
      draftId: 'draft-1',
      createdAt: '2026-10-06T10:55:48Z',
    }

    renderActions({ review })

    expect(screen.getByText('Approved')).toBeInTheDocument()
    expect(screen.getByText(/edited before approval/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Approve/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
  })

  /// ADR-010's guarantee reaching the actions: a draft whose citations did
  /// not validate cannot be approved around.
  it('offers no actions for a withheld draft', () => {
    renderActions({ draft: draft({ citationsValid: false, draftText: null }) })

    expect(screen.getByText(/citations did not validate/)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers no actions for a ticket routed to manual triage', () => {
    renderActions({ draft: null, draftEligible: false })

    expect(screen.getByText(/routed for manual triage/)).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers generation for an eligible ticket with no draft yet', async () => {
    const calls = capturePost()
    const user = userEvent.setup()

    renderActions({ draft: null, draftEligible: true })
    await user.click(screen.getByRole('button', { name: 'Generate a draft' }))

    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0].url).toContain(`/tickets/${TICKET_ID}/draft`)
  })

  it('surfaces a refusal from the server', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 409,
        statusText: 'Conflict',
        text: async () => 'This ticket has already been reviewed.',
      }),
    )

    const user = userEvent.setup()
    renderActions()
    await user.click(screen.getByRole('button', { name: 'Approve as written' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This ticket has already been reviewed.',
    )
  })

  /// The absence that matters. Nothing in this component sends anything to a
  /// customer, because no such path exists anywhere in the system.
  it('offers no action that sends the reply anywhere', () => {
    renderActions()

    const labels = screen.getAllByRole('button').map((button) => button.textContent ?? '')
    expect(labels.some((label) => /send|deliver|email|reply to customer/i.test(label))).toBe(
      false,
    )
  })
})
