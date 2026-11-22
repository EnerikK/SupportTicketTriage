import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
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

const categoryLabels = ['Billing', 'Account & Login', 'Returns & Refunds']

function reply(status: number, body: unknown) {
  return Promise.resolve({
    ok: status >= 200 && status < 300,
    status,
    statusText: '',
    json: async () => body,
    text: async () => JSON.stringify(body),
  })
}

/// Returns the queue URLs requested, which is how the paging and filter
/// tests below assert what was actually asked for rather than inspecting
/// component state. The category list is answered separately and is not
/// recorded.
function respondWith(status: number, body: unknown, categoriesStatus = 200): string[] {
  const calls: string[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn((url: string) => {
      if (url.endsWith('/categories')) {
        return reply(categoriesStatus, categoriesStatus === 200 ? categoryLabels : { detail: 'down' })
      }

      calls.push(url)
      return reply(status, body)
    }),
  )

  return calls
}

function sentCategory(url: string | undefined): string | null {
  return new URL(url ?? '', 'http://localhost').searchParams.get('category')
}

const ticket = {
  id: '01a110da-f73f-7e2f-b69f-53d2ca38bc34',
  subject: 'Charged twice',
  createdAt: '2026-10-06T10:55:48Z',
  state: 'DraftReady',
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
    // Within the table: the state filter's options carry the same words.
    const table = within(screen.getByRole('table'))
    expect(table.getByText('Billing')).toBeInTheDocument()
    expect(table.getByText('Draft ready')).toBeInTheDocument()
  })

  /// A filter changes which tickets there are, so the page number no longer
  /// means anything; page 3 of the whole queue is not page 3 of the matches.
  it('sends a chosen filter and goes back to the first page', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?page=3')
    await screen.findByRole('table')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /^Priority/ }), 'High')

    await waitFor(() => expect(calls.at(-1)).toContain('priority=High'))
    expect(calls.at(-1)).toContain('page=1')
  })

  /// The URL is text anyone can type. A value the page has no option for
  /// reads as "Any", so the dropdown never misreports what is on screen.
  it('reads an unknown filter in the address as Any', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?priority=Urgent&state=Sent')
    await screen.findByRole('table')

    expect(screen.getByRole('combobox', { name: /^Priority/ })).toHaveValue('')
    expect(screen.getByRole('combobox', { name: /^State/ })).toHaveValue('')
    expect(calls[0]).not.toMatch(/priority=|state=/)
  })

  /// The label is the value, "Account & Login" included, so it has to
  /// survive the trip through the query string.
  it('sends a chosen category by its label', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue()
    await screen.findByRole('table')

    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: /^Category/ }),
      'Account & Login',
    )

    await waitFor(() => expect(sentCategory(calls.at(-1))).toBe('Account & Login'))
  })

  /// A category in the address waits for the list, so the first request is
  /// already filtered rather than an unfiltered one followed by another.
  it('asks for a category from the address only once, filtered', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?category=Billing')
    await screen.findByRole('table')

    expect(calls).toHaveLength(1)
    expect(sentCategory(calls[0])).toBe('Billing')
  })

  it('reads a category the list does not have as Any', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?category=Refunds')
    await screen.findByRole('table')

    expect(screen.getByRole('combobox', { name: /^Category/ })).toHaveValue('')
    expect(sentCategory(calls[0])).toBeNull()
  })

  /// The queue still loads, and the missing list is named rather than left
  /// as a dropdown that quietly offers nothing.
  it('says so when the categories cannot be loaded', async () => {
    respondWith(200, queuePage([ticket]), 503)

    renderQueue('/?category=Billing')

    expect(await screen.findByRole('table')).toBeInTheDocument()
    expect(screen.getByText('Categories could not be loaded.')).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: /^Category/ })).toBeDisabled()
  })

  /// "The queue is empty" under a filter would claim there is no work.
  it('says that nothing matches, not that the queue is empty', async () => {
    respondWith(200, queuePage([], { totalCount: 0 }))

    renderQueue('/?state=Rejected')

    expect(await screen.findByText('No tickets match these filters.')).toBeInTheDocument()
    expect(screen.queryByText(/The queue is empty/)).not.toBeInTheDocument()
  })

  it('offers to clear the filters only when one is set', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?priority=High')
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))

    await waitFor(() => expect(calls.at(-1)).not.toContain('priority='))
    expect(screen.queryByRole('button', { name: 'Clear filters' })).not.toBeInTheDocument()
  })

  it('filters to a priority clicked in the table, from the first page', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue('/?page=3')
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'Show only High priority' }))

    await waitFor(() => expect(calls.at(-1)).toContain('priority=High'))
    expect(calls.at(-1)).toContain('page=1')
  })

  it('filters to a state clicked in the table', async () => {
    const calls = respondWith(200, queuePage([ticket]))

    renderQueue()
    await screen.findByRole('table')

    await userEvent.click(screen.getByRole('button', { name: 'Show only Draft ready' }))

    await waitFor(() => expect(calls.at(-1)).toContain('state=DraftReady'))
  })

  /// Every row of a filtered table shows the active value, so a button there
  /// would do nothing on every line.
  it('shows the active filter value as plain text', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue('/?priority=High&state=DraftReady')
    const table = within(await screen.findByRole('table'))

    expect(table.getByText('High')).toBeInTheDocument()
    expect(table.getByText('Draft ready')).toBeInTheDocument()
    expect(table.queryByRole('button')).not.toBeInTheDocument()
  })

  /// A pointer shortcut: the dropdowns are the keyboard route, so Tab through
  /// a row stays one stop rather than three.
  it('keeps the table values out of the tab order', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue()
    const table = within(await screen.findByRole('table'))

    for (const button of table.getAllByRole('button')) {
      expect(button).toHaveAttribute('tabindex', '-1')
    }
  })

  /// The filters add buttons to this page. No-send is an absence, so it is
  /// pinned here as it is on the dashboard and the review actions.
  it('has no button that could send anything', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue('/?priority=High')
    await screen.findByRole('table')

    // The accessible name as well as the text: a filter value's name is
    // longer than the word it shows.
    const labels = screen
      .getAllByRole('button')
      .map((button) => `${button.textContent ?? ''} ${button.getAttribute('aria-label') ?? ''}`)

    expect(labels.length).toBeGreaterThan(0)
    expect(labels.some((label) => /send|deliver|email/i.test(label))).toBe(false)
  })

  /// The table's height follows its rows, so a pager beneath it moved under
  /// the cursor between pages. Above the table, nothing it sits under
  /// depends on the page's content.
  it('puts the pager above the table', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue()

    const table = await screen.findByRole('table')
    const pager = screen.getByRole('navigation', { name: 'Queue pages' })

    expect(pager.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
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
          state: 'ManualTriage',
          category: null,
          priority: null,
          isDraftEligible: false,
          failedGates: ['ClassificationScore', 'RetrievalSimilarity'],
          draftState: 'None',
        },
      ]),
    )

    renderQueue()

    // Within the table: the state filter's options carry the same words.
    const table = within(await screen.findByRole('table'))

    expect(table.getByText('Manual triage')).toBeInTheDocument()
    expect(
      table.getByText('classifier was unsure, nothing similar resolved'),
    ).toBeInTheDocument()
    expect(table.getByText('unclassified')).toBeInTheDocument()
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

  /// Five columns do not fit a phone, so the table scrolls sideways. A
  /// scrollable region that is not focusable puts whatever is past its right
  /// edge out of reach of a keyboard entirely (WCAG 2.1.1), so the wrapper
  /// is a labelled region with a tab stop rather than a bare overflow div.
  it('puts the table in a keyboard-reachable labelled region', async () => {
    respondWith(200, queuePage([ticket]))

    renderQueue()

    const region = await screen.findByRole('region', { name: 'Ticket queue' })

    expect(region).toHaveAttribute('tabindex', '0')
    expect(region).toContainElement(screen.getByRole('table'))
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
