import { Link, useParams } from 'react-router'
import { toNumber, type TicketRouting } from '../../api/client'
import {
  useClassification,
  useDraft,
  useRouting,
  useReview,
  useSimilar,
  useTicket,
} from '../../api/queries'
import { describeGate } from '../queue/ticketState'
import { DraftPanel } from './DraftPanel'
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
            {routing.classificationScore ?? <span className="muted">none recorded</span>}
            <span className="muted"> (floor {routing.minimumClassificationScore})</span>
          </dd>
        </div>
        <div>
          <dt>Top retrieval similarity</dt>
          <dd>
            {routing.topSimilarity ?? <span className="muted">nothing retrieved</span>}
            <span className="muted"> (floor {routing.minimumSimilarity})</span>
          </dd>
        </div>
      </dl>
    </div>
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
    return <p className="status">Loading…</p>
  }

  if (ticket.error) {
    return (
      <p className="status status--error" role="alert">
        Could not load this ticket: {ticket.error.message}
      </p>
    )
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
        ← Queue
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
          <p className="status">Not classified.</p>
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
                  <span className="muted">similarity {toNumber(source.similarity).toFixed(2)}</span>
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
