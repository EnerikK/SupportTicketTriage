import { useSearchParams } from 'react-router'
import { toNumber, type Dashboard } from '../../api/client'
import { useDashboard } from '../../api/queries'
import { Alert } from '../../ui/Alert'
import { LoadingRegion, Skeleton } from '../../ui/Skeleton'
import { CategoriesPanel, DraftsPanel, ReviewsPanel, RoutingPanel } from './Breakdowns'
import {
  countDelta,
  defaultRange,
  durationDelta,
  formatDay,
  formatDuration,
  pointsDelta,
  rangeLabel,
  ranges,
  readRange,
  share,
  shareLabel,
  singleArrivalDay,
  type DashboardRange,
} from './metrics'
import { NeedsAttention } from './NeedsAttention'
import { VolumeChart } from './VolumeChart'

/**
 * Read-only pipeline health (ADR-046). Nothing on this page writes, and
 * nothing sends.
 *
 * The range lives in the address, as the queue's page does (ADR-027), so a
 * view can be linked to and survives a reload.
 */
export function DashboardPage() {
  const [params, setParams] = useSearchParams()
  const range = readRange(params.get('range'))
  const { data, isPending, error } = useDashboard(range)

  function choose(next: DashboardRange) {
    const updated = new URLSearchParams(params)

    // The default is the absence of the parameter, so it has one address
    // rather than two that render identically.
    if (next === defaultRange) {
      updated.delete('range')
    } else {
      updated.set('range', next)
    }

    setParams(updated)
  }

  return (
    <section className="dashboard" aria-labelledby="dashboard-title">
      <div className="dashboard__intro">
        <div>
          <h2 id="dashboard-title" className="dashboard__title">
            Dashboard
          </h2>
          <p className="muted">
            How tickets moved through routing, drafting and human review. Every outcome is a
            person’s decision, and nothing is sent.
          </p>
        </div>

        <div role="group" aria-label="Time range" className="range">
          {ranges.map((option) => (
            <button
              key={option}
              type="button"
              className="range__option"
              aria-pressed={option === range}
              onClick={() => choose(option)}
            >
              {rangeLabel(option)}
            </button>
          ))}
        </div>
      </div>

      {isPending ? (
        <DashboardSkeleton />
      ) : error ? (
        <Alert>Could not load the dashboard: {error.message}</Alert>
      ) : (
        <>
          <Headline data={data} range={range} />
          <Volume data={data} range={range} />
          <div className="dashboard__grid">
            <RoutingPanel routing={data.routing} range={range} />
            <ReviewsPanel reviews={data.reviews} range={range} />
            <DraftsPanel drafts={data.drafts} range={range} />
            <CategoriesPanel categories={data.categories} unclassified={data.unclassified} range={range} />
          </div>
          <NeedsAttention items={data.needsAttention} />
        </>
      )}
    </section>
  )
}

function Headline({ data, range }: { data: Dashboard; range: DashboardRange }) {
  const { current, previous } = data

  const manualNow = share(current.routedToManualTriage, current.routed)
  const manualBefore = share(previous.routedToManualTriage, previous.routed)
  const writtenNow = share(current.approvedAsWritten, current.draftDecisions)
  const writtenBefore = share(previous.approvedAsWritten, previous.draftDecisions)

  // Deltas carry their sign in the text and no colour: whether a change is
  // good depends on why it happened, which this page cannot know.
  const cards = [
    {
      label: 'Tickets received',
      value: toNumber(current.received).toLocaleString(),
      delta: countDelta(current.received, previous.received),
      hint: 'Ingested through the API',
    },
    {
      label: 'Routed to manual triage',
      value: shareLabel(current.routedToManualTriage, current.routed),
      delta: pointsDelta(manualNow, manualBefore),
      hint: 'Of tickets routed, the share that failed a gate and got no draft',
    },
    {
      label: 'Approved as written',
      value: shareLabel(current.approvedAsWritten, current.draftDecisions),
      delta: pointsDelta(writtenNow, writtenBefore),
      hint: 'Of drafts approved or rejected. Nothing is sent.',
    },
    {
      label: 'Median time to resolution',
      value:
        current.medianSecondsToResolution == null
          ? '—'
          : formatDuration(toNumber(current.medianSecondsToResolution)),
      delta: durationDelta(current.medianSecondsToResolution, previous.medianSecondsToResolution),
      hint: 'From arrival to the reviewer’s decision that resolved it',
    },
  ]

  return (
    <dl className="kpis">
      {cards.map((card) => (
        <div key={card.label} className="kpi">
          <dt className="kpi__label">{card.label}</dt>
          <dd className="kpi__value">{card.value}</dd>
          <dd className="kpi__delta">
            {card.delta} <span className="muted">vs previous {rangeLabel(range)}</span>
          </dd>
          <dd className="kpi__hint">{card.hint}</dd>
        </div>
      ))}
    </dl>
  )
}

function Volume({ data, range }: { data: Dashboard; range: DashboardRange }) {
  const anything = data.volume.some(
    (day) => toNumber(day.received) > 0 || toNumber(day.resolvedByReviewer) > 0,
  )
  const burst = singleArrivalDay(data.volume)

  return (
    <section className="panel dashboard__panel" aria-labelledby="volume-title">
      <h3 id="volume-title" className="subheading">
        Received and resolved
      </h3>

      {anything ? (
        <>
          {burst && (
            <p className="status">
              Every ticket received in this period arrived on {formatDay(burst)}, so the chart
              shows a single spike rather than a trend.
            </p>
          )}
          <VolumeChart days={data.volume} />
          <p className="muted">
            Resolved counts approvals and hand-written replies. Tickets imported straight into the
            corpus are not counted, because no one reviewed them.
          </p>
        </>
      ) : (
        <p className="status">No tickets were received or resolved in the last {rangeLabel(range)}.</p>
      )}
    </section>
  )
}

function DashboardSkeleton() {
  return (
    <LoadingRegion label="Loading the dashboard">
      <div className="kpis">
        {[0, 1, 2, 3].map((card) => (
          <div key={card} className="kpi">
            <Skeleton width="60%" />
            <Skeleton width="40%" height="1.75rem" />
          </div>
        ))}
      </div>
      <Skeleton height="15rem" />
    </LoadingRegion>
  )
}
