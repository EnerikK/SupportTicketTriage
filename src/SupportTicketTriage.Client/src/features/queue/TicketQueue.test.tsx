import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { TicketQueueItem } from '../../api/client'
import { TicketQueue } from './TicketQueue'

function renderQueue(initialEntry = '/') {
  // Retries off so a failing request surfaces immediately instead of the test
  // waiting out the default backoff.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialEntry]}>{children}</MemoryRouter>
    </QueryClientProvider>
  )

  return render(<TicketQueue />, { wrapper })
}

/// The queue answers with an envelope now, so a bare array is no longer a
/// valid response anywhere in these tests.
function queuePage(items: Partial<TicketQueueItem>[], overrides: Record<string, unknown> = {}) {
  return { items, totalCount: items.length, page: 1, pageSize: 10, ...overrides }
}

/// Returns the URLs requested, which is how the paging tests below assert
/// which page was actually asked for rather than inspecting component state.
function respondWith(status: number, body: unknown): string[] {
  const calls: string[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      calls.push(url)

      return Promise.resolve({
        ok: status >= 200 && status < 300,
        status,
        statusText: '',
        json: async () => body,
        text: async () => JSON.stringify(body),
      })
    }),
  )

  return calls
}

const ticket = {
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
}

describe('TicketQueue', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders a row per ticket with its category and state', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue()

    expect(await screen.findByRole('link', { name: 'Charged twice' })).toBeInTheDocument()
    expect(screen.getByText('Billing')).toBeInTheDocument()
    expect(screen.getByText('Draft ready')).toBeInTheDocument()
  })

  /// A gated ticket must say why it was gated. ADR-003 forbids showing the
  /// routing inputs as a confidence percentage, so the reason is words.
  it('names the failed gates on a ticket routed to manual triage', async () => {
    respondWith(
      200,
      queuePage([
        {
          ...ticket,
          id: '01a110da-f73f-7e2f-b69f-53d2ca38bc35',
          subject: 'Nothing like this before',
          category: null,
          priority: null,
          isDraftEligible: false,
          failedGates: ['ClassificationScore', 'RetrievalSimilarity'],
          draftState: 'None',
        },
      ]),
    )

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
    respondWith(200, queuePage([], { totalCount: 0 }))

    renderQueue()

    expect(await screen.findByText(/The queue is empty/)).toBeInTheDocument()
    expect(screen.getByText(/arrive through the ingest API/)).toBeInTheDocument()
  })

  /// The skeleton is layout, not content. A screen reader gets one polite
  /// "Loading the queue"; the rows of grey bars sit behind aria-hidden and
  /// must not be exposed as a table, because announcing the shape of data
  /// that has not arrived is worse than announcing nothing.
  it('announces that it is loading without exposing the skeleton', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    )

    renderQueue()

    expect(screen.getByRole('status')).toHaveTextContent('Loading the queue')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('reports the range and the page position', async () => {
    respondWith(200, queuePage([ticket], { totalCount: 301, page: 2, pageSize: 10 }))

    renderQueue('/?page=2')

    expect(await screen.findByText('Showing 11–20 of 301')).toBeInTheDocument()
    expect(screen.getByText('Page 2 of 31')).toBeInTheDocument()
  })

  it('disables previous on the first page and next on the last', async () => {
    respondWith(200, queuePage([ticket], { totalCount: 3 }))

    renderQueue()

    expect(await screen.findByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled()
  })

  /// Paging state lives in the URL, so the page a reviewer is on survives a
  /// refresh and can be sent to somebody else.
  it('asks the API for the page named in the URL', async () => {
    const calls = respondWith(200, queuePage([ticket], { totalCount: 301, page: 3, pageSize: 25 }))

    renderQueue('/?page=3&pageSize=25')

    await screen.findByRole('link', { name: 'Charged twice' })
    expect(calls[0]).toContain('page=3&pageSize=25')
  })

  /// A size the interface offers no control for is not honoured, because the
  /// dropdown would then disagree with what is on screen.
  it('ignores a page size it does not offer', async () => {
    const calls = respondWith(200, queuePage([ticket], { totalCount: 301 }))

    renderQueue('/?pageSize=37')

    await screen.findByRole('link', { name: 'Charged twice' })
    expect(calls[0]).toContain('pageSize=10')
  })

  /// Holding page 9 while moving from 10 rows to 100 lands a reviewer
  /// somewhere they never asked to be, and on a short queue lands them past
  /// the end entirely.
  it('returns to the first page when the page size changes', async () => {
    const calls = respondWith(200, queuePage([ticket], { totalCount: 301, page: 3, pageSize: 10 }))
    const user = userEvent.setup()

    renderQueue('/?page=3')
    await screen.findByRole('link', { name: 'Charged twice' })

    await user.selectOptions(screen.getByLabelText('Per page'), '25')

    await waitFor(() => expect(calls.at(-1)).toContain('page=1&pageSize=25'))
  })

  /// Reachable by typing a page number past the end. The API answers with an
  /// empty page and the true total rather than clamping, so the interface
  /// says what happened instead of pretending page 400 was page 31.
  it('says so when a page is past the end of the queue', async () => {
    respondWith(200, queuePage([], { totalCount: 301, page: 400, pageSize: 10 }))

    renderQueue('/?page=400')

    expect(await screen.findByText('There is nothing on page 400.')).toBeInTheDocument()
    expect(screen.getByText(/301 tickets/)).toBeInTheDocument()
  })
})
