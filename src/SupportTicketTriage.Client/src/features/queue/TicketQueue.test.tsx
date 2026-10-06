import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TicketQueue } from './TicketQueue'

function renderQueue() {
  // Retries off so a failing request surfaces immediately instead of the test
  // waiting out the default backoff.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  )

  return render(<TicketQueue />, { wrapper })
}

function respondWith(status: number, body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      statusText: '',
      json: async () => body,
      text: async () => JSON.stringify(body),
    }),
  )
}

describe('TicketQueue', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders a row per ticket with its category and state', async () => {
    respondWith(200, [
      {
        id: '01a110da-f73f-7e2f-b69f-53d2ca38bc34',
        subject: 'Charged twice',
        createdAt: '2026-10-06T10:55:48Z',
        category: 'Billing',
        priority: 'High',
        isDraftEligible: true,
        failedGates: [],
        draftState: 'Ready',
        reviewDecision: null,
        isResolved: false,
      },
    ])

    renderQueue()

    expect(await screen.findByRole('link', { name: 'Charged twice' })).toBeInTheDocument()
    expect(screen.getByText('Billing')).toBeInTheDocument()
    expect(screen.getByText('Draft ready')).toBeInTheDocument()
  })

  /// A gated ticket must say why it was gated. ADR-003 forbids showing the
  /// routing inputs as a confidence percentage, so the reason is words.
  it('names the failed gates on a ticket routed to manual triage', async () => {
    respondWith(200, [
      {
        id: '01a110da-f73f-7e2f-b69f-53d2ca38bc35',
        subject: 'Nothing like this before',
        createdAt: '2026-10-06T10:55:48Z',
        category: null,
        priority: null,
        isDraftEligible: false,
        failedGates: ['ClassificationScore', 'RetrievalSimilarity'],
        draftState: 'None',
        reviewDecision: null,
        isResolved: false,
      },
    ])

    renderQueue()

    expect(await screen.findByText('Manual triage')).toBeInTheDocument()
    expect(
      screen.getByText('classifier was unsure, nothing similar resolved'),
    ).toBeInTheDocument()
    expect(screen.getByText('unclassified')).toBeInTheDocument()
  })

  it('shows the failure rather than an empty queue when the request fails', async () => {
    respondWith(503, { detail: 'the database is unreachable' })

    renderQueue()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('the database is unreachable')
  })

  it('says so when there are no tickets', async () => {
    respondWith(200, [])

    renderQueue()

    expect(await screen.findByText(/No tickets yet/)).toBeInTheDocument()
  })
})
