import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Dashboard } from '../../api/client'
import { DashboardPage } from './DashboardPage'

function renderDashboard(initialEntry = '/dashboard') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialEntry]}>{children}</MemoryRouter>
    </QueryClientProvider>
  )

  return render(<DashboardPage />, { wrapper })
}

/// Returns the URLs requested, so the tests assert which range was actually
/// asked for rather than inspecting component state.
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

function days(count: number, received: (index: number) => number = () => 0) {
  return Array.from({ length: count }, (_, index) => ({
    day: `2026-11-${String(index + 1).padStart(2, '0')}`,
    received: received(index),
    resolvedByReviewer: 0,
  }))
}

const nothing = {
  received: 0,
  routed: 0,
  routedToManualTriage: 0,
  draftDecisions: 0,
  approvedAsWritten: 0,
  medianSecondsToResolution: null,
}

/// The shape a freshly seeded database produces: three hundred tickets that
/// all arrived on one day, and nothing in the period before.
function dashboard(overrides: Partial<Dashboard> = {}): Dashboard {
  return {
    range: '30d',
    from: '2026-11-01T00:00:00+00:00',
    to: '2026-12-01T00:00:00+00:00',
    previousFrom: '2026-10-02T00:00:00+00:00',
    current: {
      received: 300,
      routed: 62,
      routedToManualTriage: 15,
      draftDecisions: 1,
      approvedAsWritten: 1,
      medianSecondsToResolution: 3189.2,
    },
    previous: nothing,
    volume: days(30, (index) => (index === 17 ? 300 : 0)),
    routing: { bothPassed: 0, retrievalSimilarityFailed: 0, classificationScoreFailed: 0, bothFailed: 0, notTriaged: 0 },
    reviews: { approvedAsWritten: 0, editedThenApproved: 0, rejected: 0, resolvedManually: 0 },
    drafts: { generated: 0, withheld: 0, validCitingNoSource: 0, averageCitedPerValidDraft: null },
    categories: [],
    unclassified: 0,
    needsAttention: [],
    ...overrides,
  }
}

/// The value and the change shown for the card with this label.
function card(label: string) {
  const term = screen.getByText(label, { selector: 'dt' })
  const values = within(term.parentElement!).getAllByRole('definition')
  return { value: values[0].textContent, delta: values[1].textContent }
}

describe('DashboardPage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('asks for thirty days by default and shows the headline figures', async () => {
    const calls = respondWith(200, dashboard())

    renderDashboard()

    await screen.findByText('Tickets received')
    expect(calls[0]).toMatch(/\/dashboard\?range=30d$/)
    expect(card('Tickets received').value).toBe('300')
    expect(card('Routed to manual triage').value).toBe('24%')
    expect(card('Approved as written').value).toBe('100%')
    expect(card('Median time to resolution').value).toBe('53 min')
  })

  /// With nothing in the previous period there is nothing honest to compare
  /// against, so every card says so rather than inventing a percentage.
  it('shows no comparison when the previous period had nothing', async () => {
    respondWith(200, dashboard())

    renderDashboard()

    await screen.findByText('Tickets received')
    for (const label of ['Tickets received', 'Routed to manual triage', 'Approved as written', 'Median time to resolution']) {
      expect(card(label).delta).toMatch(/^— vs previous 30 days$/)
    }
  })

  it('reports each change in the unit that fits it', async () => {
    respondWith(
      200,
      dashboard({
        current: { received: 120, routed: 100, routedToManualTriage: 25, draftDecisions: 2, approvedAsWritten: 1, medianSecondsToResolution: 3600 },
        previous: { received: 100, routed: 100, routedToManualTriage: 20, draftDecisions: 1, approvedAsWritten: 1, medianSecondsToResolution: 4800 },
      }),
    )

    renderDashboard()

    await screen.findByText('Tickets received')
    expect(card('Tickets received').delta).toMatch(/^\+20%/)
    expect(card('Routed to manual triage').delta).toMatch(/^\+5 pts/)
    expect(card('Approved as written').delta).toMatch(/^−50 pts/)
    expect(card('Median time to resolution').delta).toMatch(/^−20 min/)
  })

  /// The generated types allow any count to arrive as a numeric string.
  it('reads counts that arrive as strings', async () => {
    respondWith(
      200,
      dashboard({
        current: { received: '300', routed: '62', routedToManualTriage: '15', draftDecisions: '1', approvedAsWritten: '1', medianSecondsToResolution: '3189.2' },
      }),
    )

    renderDashboard()

    await screen.findByText('Tickets received')
    expect(card('Tickets received').value).toBe('300')
    expect(card('Routed to manual triage').value).toBe('24%')
    expect(card('Median time to resolution').value).toBe('53 min')
  })

  it('asks for the range chosen, and shows it as the one pressed', async () => {
    const calls = respondWith(200, dashboard())
    const user = userEvent.setup()

    renderDashboard()
    await screen.findByText('Tickets received')

    await user.click(screen.getByRole('button', { name: '7 days' }))

    await waitFor(() => expect(calls.at(-1)).toMatch(/\/dashboard\?range=7d$/))
    expect(screen.getByRole('button', { name: '7 days' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: '30 days' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('falls back to thirty days for a range it does not know', async () => {
    const calls = respondWith(200, dashboard())

    renderDashboard('/dashboard?range=1d')

    await screen.findByText('Tickets received')
    expect(calls[0]).toMatch(/\/dashboard\?range=30d$/)
    expect(screen.getByRole('button', { name: '30 days' })).toHaveAttribute('aria-pressed', 'true')
  })

  /// ADR-046: a single burst is named, not drawn as though it were a trend.
  it('says so when every ticket arrived on one day', async () => {
    respondWith(200, dashboard())

    renderDashboard()

    expect(await screen.findByText(/single spike rather than a trend/)).toBeInTheDocument()
  })

  it('says nothing of a spike when tickets arrived on several days', async () => {
    respondWith(200, dashboard({ volume: days(30, (index) => (index % 3 === 0 ? 4 : 0)) }))

    renderDashboard()

    await screen.findByText('Tickets received')
    expect(screen.queryByText(/single spike/)).not.toBeInTheDocument()
  })

  it('names the chart and offers its data as a table', async () => {
    respondWith(200, dashboard())

    renderDashboard()

    expect(
      await screen.findByRole('img', { name: /received and resolved by a reviewer per day.*300 received, 0 resolved/i }),
    ).toBeInTheDocument()

    const table = screen.getByRole('table', { name: 'Tickets per day' })
    // One row per day, plus the header row.
    expect(within(table).getAllByRole('row')).toHaveLength(31)
  })

  it('says so when nothing happened in the period', async () => {
    respondWith(200, dashboard({ current: nothing, volume: days(30) }))

    renderDashboard()

    expect(
      await screen.findByText('No tickets were received or resolved in the last 30 days.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('surfaces a failure as an alert', async () => {
    respondWith(500, { title: 'The database is unavailable.' })

    renderDashboard()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Could not load the dashboard: The database is unavailable.',
    )
  })

  /// The same absence the review actions pin: nothing here sends anything.
  it('offers no action that sends anything', async () => {
    respondWith(200, dashboard())

    renderDashboard()

    await screen.findByText('Tickets received')
    const labels = screen.getAllByRole('button').map((button) => button.textContent ?? '')
    expect(labels).toEqual(['7 days', '30 days', '90 days'])
    expect(labels.some((label) => /send|deliver|email/i.test(label))).toBe(false)
  })
})
