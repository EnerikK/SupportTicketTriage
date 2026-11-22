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

  describe('breakdowns', () => {
    /// The row a label belongs to, so its count and share can be read from it.
    function rowOf(label: string) {
      return within(screen.getByText(label, { selector: '.bars__label' }).closest('li')!)
    }

    /// Shares are of routed tickets only, the headline's population, so the
    /// five untriaged tickets here must not dilute them: 2 of 10, not 2 of 15.
    it('names each routing outcome in the queue’s words, sharing it among routed tickets only', async () => {
      respondWith(
        200,
        dashboard({
          routing: { bothPassed: 6, retrievalSimilarityFailed: 2, classificationScoreFailed: 1, bothFailed: 1, notTriaged: 5 },
        }),
      )

      renderDashboard()

      await screen.findByText('Routing outcomes')
      expect(rowOf('Manual triage: nothing similar resolved').getByText('2')).toBeInTheDocument()
      expect(rowOf('Manual triage: nothing similar resolved').getByText('20%')).toBeInTheDocument()
      expect(rowOf('Manual triage: classifier was unsure and nothing similar resolved').getByText('1')).toBeInTheDocument()

      // Untriaged tickets are reported beside the shares, not as a row in them.
      expect(screen.queryByText('Not triaged: no routing decision')).not.toBeInTheDocument()
      expect(screen.getByText(/with no routing decision, left out of the shares above/)).toHaveTextContent(
        'Not triaged: 5 tickets',
      )
    })

    /// 1 of 300 is not 0%, and 299 of 300 is not 100%.
    it('never rounds a share to a claim it cannot make', async () => {
      respondWith(
        200,
        dashboard({
          routing: { bothPassed: 299, retrievalSimilarityFailed: 1, classificationScoreFailed: 0, bothFailed: 0, notTriaged: 0 },
        }),
      )

      renderDashboard()

      await screen.findByText('Routing outcomes')
      expect(rowOf('Draft-eligible: both gates passed').getByText('>99%')).toBeInTheDocument()
      expect(rowOf('Manual triage: nothing similar resolved').getByText('<1%')).toBeInTheDocument()
      expect(rowOf('Manual triage: classifier was unsure').getByText('0%')).toBeInTheDocument()
    })

    /// "Approved as written" has one percentage, in its headline card. A
    /// share here, over all four decisions, would put a second on the label.
    it('counts all four review decisions without a second share of them', async () => {
      respondWith(
        200,
        dashboard({ reviews: { approvedAsWritten: 1, editedThenApproved: 1, rejected: 1, resolvedManually: '2' } }),
      )

      renderDashboard()

      expect(await screen.findByText(/5 decisions/)).toBeInTheDocument()
      expect(rowOf('Resolved by hand').getByText('2')).toBeInTheDocument()
      expect(rowOf('Approved as written').queryByText(/%/)).not.toBeInTheDocument()
    })

    /// The failures are counted beside the rest rather than left out.
    it('shows withheld drafts and drafts citing nothing beside the rest', async () => {
      respondWith(
        200,
        dashboard({ drafts: { generated: 4, withheld: 1, validCitingNoSource: 1, averageCitedPerValidDraft: 0.5 } }),
      )

      renderDashboard()

      expect(await screen.findByText('25% of drafts, never shown to a reviewer')).toBeInTheDocument()
      expect(screen.getByText('Valid drafts citing no source')).toBeInTheDocument()
      expect(screen.getByText('0.5')).toBeInTheDocument()
    })

    it('shows no average when no draft passed validation', async () => {
      respondWith(
        200,
        dashboard({ drafts: { generated: 1, withheld: 1, validCitingNoSource: 0, averageCitedPerValidDraft: null } }),
      )

      renderDashboard()

      const label = await screen.findByText('Sources cited per valid draft')
      expect(within(label.parentElement!).getAllByRole('definition')[0]).toHaveTextContent('—')
    })

    it('lists every category the API names, with the unclassified apart', async () => {
      respondWith(
        200,
        dashboard({
          categories: [
            { category: 'Billing', count: 1 },
            { category: 'Account & Login', count: 3 },
          ],
          unclassified: 2,
        }),
      )

      renderDashboard()

      await screen.findByText('Tickets by category')
      expect(rowOf('Account & Login').getByText('3')).toBeInTheDocument()
      expect(rowOf('Unclassified').getByText('2')).toBeInTheDocument()
    })
  })

  describe('needs attention', () => {
    const item = (overrides: Partial<Dashboard['needsAttention'][number]>) => ({
      ticketId: 'ticket-1',
      subject: 'A subject',
      receivedAt: '2026-11-18T09:00:00+00:00',
      category: null,
      reason: 'NotTriaged',
      failedGates: [],
      ...overrides,
    })

    /// A rejected ticket says outright that nothing can be done with it yet,
    /// rather than looking like work waiting to be picked up.
    it('names the reason for each ticket and links to it', async () => {
      respondWith(
        200,
        dashboard({
          needsAttention: [
            item({ ticketId: 'rejected', subject: 'Rejected one', reason: 'DraftRejected', category: 'Account & Login' }),
            item({ ticketId: 'withheld', subject: 'Withheld one', reason: 'DraftWithheld' }),
            item({ ticketId: 'gated', subject: 'Gated one', reason: 'GateFailed', failedGates: ['RetrievalSimilarity'] }),
            item({ ticketId: 'untriaged', subject: 'Untriaged one', reason: 'NotTriaged' }),
          ],
        }),
      )

      renderDashboard()

      const table = within(await screen.findByRole('region', { name: 'Tickets that need attention' }))
      expect(table.getByText('Draft rejected — no action available yet')).toBeInTheDocument()
      expect(table.getByText('Draft withheld: invalid citations')).toBeInTheDocument()
      expect(table.getByText('Manual triage: nothing similar resolved')).toBeInTheDocument()
      expect(table.getByText('Not triaged')).toBeInTheDocument()
      expect(table.getByText('Account & Login')).toBeInTheDocument()
      expect(table.getByRole('link', { name: 'Gated one' })).toHaveAttribute('href', '/tickets/gated')
    })

    it('says so when nothing needs attention', async () => {
      respondWith(200, dashboard({ needsAttention: [] }))

      renderDashboard()

      expect(await screen.findByText(/Nothing needs attention/)).toBeInTheDocument()
      expect(screen.queryByRole('region', { name: 'Tickets that need attention' })).not.toBeInTheDocument()
    })
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
