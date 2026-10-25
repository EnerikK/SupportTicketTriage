import { ArrowLeft } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { type TicketRouting } from '../../api/client'
import {
  useClassification,
  useDraft,
  useRouting,
  useReview,
  useTriage,
  useSimilar,
  useTicket,
} from '../../api/queries'
import { Alert } from '../../ui/Alert'
import { LoadingRegion, Skeleton } from '../../ui/Skeleton'
import { describeGate } from '../queue/ticketState'
import { DraftPanel } from './DraftPanel'
import { formatScore, similarityLabel } from './score'
import { ReviewActions } from './ReviewActions'

function RoutingSummary({ routing }: { routing: TicketRouting | null }) {
  if (!routing) {
    return <p className="status">This ticket has not been triaged yet.</p>
  }

  return (
    <div className="panel">
      <p>
        {routing.isDraftEligible ? (
          <strong className="state--ready">Eligible for a drafted reply.</strong>
        ) : (
          <>
            <strong className="state--blocked">Routed for manual triage.</strong>{' '}
            {routing.failedGates.map(describeGate).join('; ')}.
          </>
        )}
      </p>

      {/* Both gates are shown with the floor that was applied, because ADR-009
          stores the thresholds on the decision precisely so it stays readable
          after they are retuned. Labelled "self-reported" and never called
          confidence or shown as a percentage - it is not a probability. */}
      <dl className="gates-detail">
        <div>
          <dt>Classifier self-reported score</dt>
          <dd>
            {routing.classificationScore === null || routing.classificationScore === undefined ? (
              <span className="muted">none recorded</span>
            ) : (
              formatScore(routing.classificationScore)
            )}
            <span className="muted"> (floor {routing.minimumClassificationScore})</span>
          </dd>
        </div>
        <div>
          <dt>Top retrieval similarity</dt>
          <dd>
            {routing.topSimilarity === null || routing.topSimilarity === undefined ? (
              <span className="muted">nothing retrieved</span>
            ) : (
              formatScore(routing.topSimilarity)
            )}
            <span className="muted"> (floor {routing.minimumSimilarity})</span>
          </dd>
        </div>
      </dl>
    </div>
  )
}

/// A ticket with no classification never finished triage, and until the
/// triage endpoint existed it never would have.
///
/// Absent classification is the signal, not absent routing. Ingest routes
/// whether or not classification succeeded - a ticket it could not classify
/// is exactly one a human should look at - so a stuck ticket has a routing
/// decision with its gates failed and no classification at all. Keying off
/// routing would miss every ticket this control exists to rescue.
function UntriagedTicket({ ticketId }: { ticketId: string }) {
  const triage = useTriage(ticketId)

  return (
    <div className="panel">
      <p className="untriaged__text">
        Not classified. This ticket did not finish triage, so it has no category, no
        priority, and no routing decision that means anything.
      </p>

      <button type="button" onClick={() => triage.mutate()} disabled={triage.isPending}>
        {triage.isPending ? 'Triaging…' : 'Triage this ticket'}
      </button>

      {triage.error && <Alert>{triage.error.message}</Alert>}
    </div>
  )
}

/// Mirrors the real page: a subject, then the stack of panels. The detail
/// view fires five requests, so unlike the queue this skeleton is seen for a
/// real interval against a deployed API rather than only in a screenshot.
function DetailSkeleton() {
  return (
    <LoadingRegion label="Loading the ticket">
      <Skeleton width="45%" height="1.75rem" />
      <div className="detail-skeleton">
        {[3, 2, 2].map((lines, panel) => (
          <div key={panel} className="panel detail-skeleton__panel">
            {Array.from({ length: lines }, (_, line) => (
              <Skeleton key={line} width={line === lines - 1 ? '55%' : '100%'} />
            ))}
          </div>
        ))}
      </div>
    </LoadingRegion>
  )
}

export function TicketDetail() {
  const { id = '' } = useParams()

  // Five independent requests. Each stage answers 404 for a ticket that has
  // not reached it, so "absent" and "failed" never get confused - which is
  // what made a hand-rolled fetching layer the wrong trade here.
  const ticket = useTicket(id)
  const classification = useClassification(id)
  const routing = useRouting(id)
  const similar = useSimilar(id)
  const draft = useDraft(id)
  const review = useReview(id)

  if (ticket.isPending) {
    return <DetailSkeleton />
  }

  if (ticket.error) {
    return <Alert>Could not load this ticket: {ticket.error.message}</Alert>
  }

  if (!ticket.data) {
    return (
      <p className="status">
        No such ticket. <Link to="/">Back to the queue</Link>
      </p>
    )
  }

  return (
    <article className="detail">
      <Link to="/" className="detail__back">
        <ArrowLeft size={15} aria-hidden="true" focusable="false" />
        Queue
      </Link>

      <h2 className="detail__subject">{ticket.data.subject}</h2>

      <section>
        <h3 className="subheading">Ticket</h3>
        <p className="panel detail__body">{ticket.data.body}</p>
      </section>

      <section>
        <h3 className="subheading">Classification</h3>
        {classification.data ? (
          <p className="panel">
            <strong>{classification.data.category}</strong> · {classification.data.priority}
            <span className="muted provenance">
              {classification.data.chatModel} · prompt {classification.data.promptVersion}
            </span>
          </p>
        ) : (
          <UntriagedTicket ticketId={id} />
        )}
      </section>

      <section>
        <h3 className="subheading">Routing</h3>
        <RoutingSummary routing={routing.data ?? null} />
      </section>

      <section>
        <h3 className="subheading">Retrieved evidence</h3>
        {similar.data && similar.data.length > 0 ? (
          <ul className="evidence">
            {similar.data.map((source, index) => (
              <li key={source.ticketId} className="panel">
                <p className="evidence__head">
                  <span className="citations__rank">#{index + 1}</span> {source.subject}{' '}
                  <span className="muted">{similarityLabel(source.similarity)}</span>
                </p>
                <p className="evidence__resolution">{source.resolution}</p>
              </li>
            ))}
          </ul>
        ) : (
          <p className="status">
            Nothing similar has been resolved, so there is nothing to ground a reply on.
          </p>
        )}
      </section>

      <section>
        <h3 className="subheading">Draft reply</h3>
        <DraftPanel
          draft={draft.data ?? null}
          evidence={similar.data ?? []}
          draftEligible={routing.data?.isDraftEligible ?? null}
        />
      </section>

      <section>
        <h3 className="subheading">Review</h3>
        <ReviewActions
          ticketId={id}
          draft={draft.data ?? null}
          review={review.data ?? null}
          draftEligible={routing.data?.isDraftEligible ?? null}
        />
      </section>
    </article>
  )
}
