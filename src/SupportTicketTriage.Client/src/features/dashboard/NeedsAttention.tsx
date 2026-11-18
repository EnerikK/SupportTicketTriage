import { Link } from 'react-router'
import type { Dashboard } from '../../api/client'
import { describeCategory, formatDate } from '../../ui/format'
import { StateIcon } from '../queue/StateIcon'
import { describeGate, type TicketStateTone } from '../queue/ticketState'

type AttentionItem = Dashboard['needsAttention'][number]

/// Every reason is named in the same words the rest of the interface uses.
/// A rejected ticket says outright that nothing can be done with it yet,
/// rather than looking like work waiting to be picked up.
function describeReason(item: AttentionItem): { tone: TicketStateTone; text: string } {
  switch (item.reason) {
    case 'DraftRejected':
      return { tone: 'rejected', text: 'Draft rejected — no action available yet' }
    case 'DraftWithheld':
      return { tone: 'blocked', text: 'Draft withheld: invalid citations' }
    case 'GateFailed':
      return { tone: 'blocked', text: `Manual triage: ${item.failedGates.map(describeGate).join(' and ')}` }
    case 'NotTriaged':
      return { tone: 'pending', text: 'Not triaged' }
    default:
      return { tone: 'pending', text: item.reason }
  }
}

/// Current state, whatever the range: the newest unresolved tickets with no
/// draft a reviewer can approve, at most ten (ADR-046).
export function NeedsAttention({ items }: { items: Dashboard['needsAttention'] }) {
  return (
    <section className="panel dashboard__panel" aria-labelledby="attention-title">
      <div className="dashboard__panel-head">
        <h3 id="attention-title" className="subheading">
          Needs attention
        </h3>
        <Link to="/">Open the queue</Link>
      </div>
      <p className="muted">
        Unresolved tickets with no draft a reviewer can approve, newest first. This is the current state,
        whatever the range.
      </p>

      {items.length === 0 ? (
        <p className="status">
          Nothing needs attention: every unresolved ticket has a draft a reviewer can act on, or can still be
          drafted.
        </p>
      ) : (
        // The same scrolling region and table styles as the queue, so the two
        // tables look and behave alike, phone width included.
        <div className="queue-scroll" tabIndex={0} role="region" aria-label="Tickets that need attention">
          <table className="queue">
            <thead>
              <tr>
                <th scope="col">Subject</th>
                <th scope="col">Category</th>
                <th scope="col">Why there is no draft to approve</th>
                <th scope="col">Received</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const reason = describeReason(item)

                return (
                  <tr key={item.ticketId}>
                    <td>
                      <Link to={`/tickets/${item.ticketId}`}>{item.subject}</Link>
                    </td>
                    <td>
                      {item.category ? (
                        describeCategory(item.category)
                      ) : (
                        <span className="muted">unclassified</span>
                      )}
                    </td>
                    <td>
                      <span className={`state state--${reason.tone}`}>
                        <StateIcon tone={reason.tone} />
                        {reason.text}
                      </span>
                    </td>
                    <td>{formatDate(item.receivedAt)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
