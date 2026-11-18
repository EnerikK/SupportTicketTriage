import { TriangleAlert } from 'lucide-react'
import { toNumber, type Dashboard } from '../../api/client'
import { describeCategory } from '../../ui/format'
import { describeGate } from '../queue/ticketState'
import { BarList } from './BarList'
import { percent, rangeLabel, share, type DashboardRange } from './metrics'

/// The breakdown panels, all for the selected range only (ADR-046).

export function RoutingPanel({ routing, range }: { routing: Dashboard['routing']; range: DashboardRange }) {
  const unsure = describeGate('ClassificationScore')
  const nothingSimilar = describeGate('RetrievalSimilarity')

  return (
    <section className="panel dashboard__panel" aria-labelledby="routing-title">
      <h3 id="routing-title" className="subheading">
        Routing outcomes
      </h3>
      <p className="muted">
        Last {rangeLabel(range)}. Both gates must pass before a draft is generated. A failed gate is
        named, not shown as a probability.
      </p>
      <BarList
        label="Routing outcomes"
        withShare
        bars={[
          { label: 'Draft-eligible: both gates passed', count: routing.bothPassed, tone: 'ready' },
          { label: `Manual triage: ${nothingSimilar}`, count: routing.retrievalSimilarityFailed, tone: 'blocked' },
          { label: `Manual triage: ${unsure}`, count: routing.classificationScoreFailed, tone: 'blocked' },
          { label: `Manual triage: ${unsure} and ${nothingSimilar}`, count: routing.bothFailed, tone: 'blocked' },
          { label: 'Not triaged: no routing decision', count: routing.notTriaged, tone: 'muted' },
        ]}
      />
    </section>
  )
}

export function ReviewsPanel({ reviews, range }: { reviews: Dashboard['reviews']; range: DashboardRange }) {
  const total =
    toNumber(reviews.approvedAsWritten) +
    toNumber(reviews.editedThenApproved) +
    toNumber(reviews.rejected) +
    toNumber(reviews.resolvedManually)

  return (
    <section className="panel dashboard__panel" aria-labelledby="reviews-title">
      <h3 id="reviews-title" className="subheading">
        Review decisions
      </h3>
      <p className="muted">
        Last {rangeLabel(range)}: {total.toLocaleString()} {total === 1 ? 'decision' : 'decisions'}. A
        person decides every outcome, and nothing is sent.
      </p>
      <BarList
        label="Review decisions"
        withShare
        bars={[
          { label: 'Approved as written', count: reviews.approvedAsWritten, tone: 'ready' },
          { label: 'Edited, then approved', count: reviews.editedThenApproved, tone: 'ready' },
          { label: 'Rejected', count: reviews.rejected, tone: 'rejected' },
          { label: 'Resolved by hand', count: reviews.resolvedManually, tone: 'accent' },
        ]}
      />
      <p className="muted">
        Resolved by hand: a ticket with no draft a reviewer could judge, answered in the reviewer’s own
        words.
      </p>
    </section>
  )
}

/**
 * Withheld drafts and valid drafts citing nothing are counted beside the
 * rest, and flagged when there are any, because hiding them is what ADR-046
 * rules out. The average is "—" when no draft passed validation: zero would
 * read as "they cited nothing".
 */
export function DraftsPanel({ drafts, range }: { drafts: Dashboard['drafts']; range: DashboardRange }) {
  const generated = toNumber(drafts.generated)
  const withheld = toNumber(drafts.withheld)
  const citingNothing = toNumber(drafts.validCitingNoSource)
  const average = drafts.averageCitedPerValidDraft

  const stats = [
    { label: 'Drafts generated', value: generated.toLocaleString(), note: 'For draft-eligible tickets', warn: false },
    {
      label: 'Withheld: invalid citations',
      value: withheld.toLocaleString(),
      note: `${percent(share(withheld, generated))} of drafts, never shown to a reviewer`,
      warn: withheld > 0,
    },
    {
      label: 'Valid drafts citing no source',
      value: citingNothing.toLocaleString(),
      note: 'Shown with a warning that nothing supports them',
      warn: citingNothing > 0,
    },
    {
      label: 'Sources cited per valid draft',
      value: average == null ? '—' : toNumber(average).toFixed(1),
      note: 'On average',
      warn: false,
    },
  ]

  return (
    <section className="panel dashboard__panel" aria-labelledby="drafts-title">
      <h3 id="drafts-title" className="subheading">
        Drafts and citations
      </h3>
      <p className="muted">
        Last {rangeLabel(range)}. Every cited ticket is checked against the draft’s own retrieval set.
      </p>
      <dl className="stats">
        {stats.map((stat) => (
          <div key={stat.label} className={stat.warn ? 'stat stat--warn' : 'stat'}>
            <dt className="stat__label">{stat.label}</dt>
            <dd className="stat__value">
              {stat.warn && <TriangleAlert className="stat__icon" size={16} aria-hidden="true" focusable="false" />}
              {stat.value}
            </dd>
            <dd className="stat__note">{stat.note}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

export function CategoriesPanel({
  categories,
  unclassified,
  range,
}: {
  categories: Dashboard['categories']
  unclassified: Dashboard['unclassified']
  range: DashboardRange
}) {
  return (
    <section className="panel dashboard__panel" aria-labelledby="categories-title">
      <h3 id="categories-title" className="subheading">
        Tickets by category
      </h3>
      <p className="muted">
        Received in the last {rangeLabel(range)}, by their latest classification. Categories are never
        overridden.
      </p>
      <BarList
        label="Tickets by category"
        bars={[
          ...categories.map((category) => ({
            label: describeCategory(category.category),
            count: category.count,
            tone: 'accent' as const,
          })),
          { label: 'Unclassified', count: unclassified, tone: 'muted' as const },
        ]}
      />
    </section>
  )
}
