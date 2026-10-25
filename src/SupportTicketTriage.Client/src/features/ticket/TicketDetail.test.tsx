import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TicketDetail } from './TicketDetail'

const TICKET_ID = '01a110da-f73f-7e2f-b69f-53d2ca38bc34'

/// Each stage is a separate request and each answers 404 when the ticket has
/// not reached it. Routing by URL here means a test can withhold one stage
/// without affecting the others, which is the behaviour under test.
function routeResponses(responses: Record<string, { status: number; body?: unknown }>) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      const match = Object.keys(responses).find((suffix) => url.endsWith(suffix))
      const response = match ? responses[match] : { status: 404 }

      return Promise.resolve({
        ok: response.status >= 200 && response.status < 300,
        status: response.status,
        statusText: '',
        json: async () => response.body,
        text: async () => JSON.stringify(response.body ?? ''),
      })
    }),
  )
}

function renderDetail() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )

  return render(
    <MemoryRouter initialEntries={[`/tickets/${TICKET_ID}`]}>
      <Routes>
        <Route path="/tickets/:id" element={<TicketDetail />} />
      </Routes>
    </MemoryRouter>,
    { wrapper },
  )
}

const ticketBody = {
  id: TICKET_ID,
  subject: 'Charged twice',
  body: 'My card was charged twice for one order.',
  createdAt: '2026-10-06T10:55:48Z',
}

const classificationBody = {
  category: 'Billing',
  priority: 'High',
  chatModel: 'gpt-4o-mini',
  promptVersion: 'v1',
  createdAt: '2026-10-06T10:55:48Z',
}

const routingBody = {
  isDraftEligible: true,
  failedGates: [],
  classificationScore: 0.91,
  topSimilarity: 0.78,
  minimumClassificationScore: 0.5,
  minimumSimilarity: 0.6,
  createdAt: '2026-10-06T10:55:48Z',
}

describe('TicketDetail', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  /// A ticket that has been ingested but not yet classified, routed or
  /// drafted is the ordinary state right after intake, and every one of those
  /// stages answers 404. None of them is an error and the page must still
  /// render.
  it('renders a ticket whose later stages have all 404ed', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
    })

    renderDetail()

    expect(await screen.findByText('Charged twice')).toBeInTheDocument()
    expect(screen.getByText('Not classified.')).toBeInTheDocument()
    expect(screen.getByText(/did not finish triage/)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  /// The recovery path. A ticket with no classification never finished
  /// triage, and before the endpoint existed there was nothing to be done
  /// about it from any interface.
  it('offers to triage a ticket that was never classified', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
    })

    renderDetail()

    expect(await screen.findByRole('button', { name: 'Triage this ticket' })).toBeInTheDocument()
  })

  /// Absent classification is the signal, not absent routing: ingest routes
  /// whether or not classification succeeded, so a stuck ticket has a routing
  /// decision with its gates failed. Keying off routing would miss it.
  it('still offers triage when routing exists but classification does not', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
      '/routing': {
        status: 200,
        body: {
          isDraftEligible: false,
          failedGates: ['ClassificationScore'],
          classificationScore: null,
          topSimilarity: null,
          minimumClassificationScore: 0.5,
          minimumSimilarity: 0.6,
          createdAt: '2026-10-06T10:55:48Z',
        },
      },
    })

    renderDetail()

    expect(await screen.findByRole('button', { name: 'Triage this ticket' })).toBeInTheDocument()
  })

  /// The case the first version of this control missed entirely.
  ///
  /// The evaluation harness classifies every ticket it seeds and routes only
  /// the handful it scores, so a seeded corpus is full of tickets that are
  /// classified and unrouted. Keying the control off absent classification
  /// alone left every one of them a dead end.
  it('offers triage when the ticket was classified but never routed', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
      '/classification': { status: 200, body: classificationBody },
    })

    renderDetail()

    expect(await screen.findByText('Billing')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Triage this ticket' })).toBeInTheDocument()
    expect(screen.getByText(/classified but never routed/)).toBeInTheDocument()
  })

  it('offers no triage once both stages have run', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
      '/classification': { status: 200, body: classificationBody },
      '/routing': { status: 200, body: routingBody },
    })

    renderDetail()

    expect(await screen.findByText('Billing')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Triage this ticket' })).not.toBeInTheDocument()
  })

  it('says so when the ticket itself does not exist', async () => {
    routeResponses({})

    renderDetail()

    expect(await screen.findByText(/No such ticket/)).toBeInTheDocument()
  })

  /// ADR-003: the gates are named, and the stored floors are shown beside the
  /// inputs so the decision stays readable after the floors are retuned.
  it('shows why a gated ticket was routed to manual triage', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': { status: 200, body: [] },
      '/routing': {
        status: 200,
        body: {
          isDraftEligible: false,
          failedGates: ['ClassificationScore', 'RetrievalSimilarity'],
          classificationScore: 0.42,
          topSimilarity: null,
          minimumClassificationScore: 0.7,
          minimumSimilarity: 0.6,
          createdAt: '2026-10-06T10:55:48Z',
        },
      },
    })

    renderDetail()

    expect(await screen.findByText(/Routed for manual triage/)).toBeInTheDocument()
    expect(
      screen.getByText(/classifier was unsure; nothing similar resolved/),
    ).toBeInTheDocument()
    expect(screen.getByText('0.42')).toBeInTheDocument()
    expect(screen.getByText('(floor 0.7)')).toBeInTheDocument()
    expect(screen.getByText('nothing retrieved')).toBeInTheDocument()
  })

  it('lists the retrieved evidence with its resolutions', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': {
        status: 200,
        body: [
          {
            ticketId: 'source-1',
            subject: 'Duplicate charge',
            redactedText: 'redacted',
            resolution: 'The second charge was an authorisation hold.',
            similarity: 0.91,
          },
        ],
      },
    })

    renderDetail()

    expect(await screen.findByText('Duplicate charge')).toBeInTheDocument()
    expect(
      screen.getByText('The second charge was an authorisation hold.'),
    ).toBeInTheDocument()
  })

  /// The API types doubles as number-or-numeric-string, so the client has to
  /// cope with the string form the contract permits.
  it('formats a similarity that arrives as a string', async () => {
    routeResponses({
      [`/tickets/${TICKET_ID}`]: { status: 200, body: ticketBody },
      '/similar': {
        status: 200,
        body: [
          {
            ticketId: 'source-1',
            subject: 'Duplicate charge',
            redactedText: 'redacted',
            resolution: 'A resolution.',
            similarity: '0.9137',
          },
        ],
      },
    })

    renderDetail()

    expect(await screen.findByText('similarity 0.91')).toBeInTheDocument()
  })
})
