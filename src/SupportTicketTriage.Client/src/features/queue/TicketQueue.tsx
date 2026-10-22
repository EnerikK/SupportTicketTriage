import { Inbox } from 'lucide-react'
import { Link } from 'react-router'
import { useQueue } from '../../api/queries'
import { Alert } from '../../ui/Alert'
import { LoadingRegion, Skeleton } from '../../ui/Skeleton'
import { StateIcon } from './StateIcon'
import { describeGate, describeState } from './ticketState'

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

/// Shared by the table and its skeleton so the two cannot drift apart and
/// make the layout jump on load, which is the only thing a skeleton is for.
const columns = ['Subject', 'Category', 'Priority', 'State', 'Received'] as const

/// Fixed, not random. Varied widths read as text; widths that reshuffle on
/// every render read as flicker.
const skeletonWidths: Record<(typeof columns)[number], string> = {
  Subject: '72%',
  Category: '45%',
  Priority: '35%',
  State: '58%',
  Received: '66%',
}

const skeletonRows = 8

function QueueHeader() {
  return (
    <thead>
      <tr>
        {columns.map((column) => (
          <th key={column} scope="col">
            {column}
          </th>
        ))}
      </tr>
    </thead>
  )
}

function QueueSkeleton() {
  return (
    <LoadingRegion label="Loading the queue">
      <table className="queue">
        <caption className="queue__caption">
          <Skeleton width="5rem" />
        </caption>
        <QueueHeader />
        <tbody>
          {Array.from({ length: skeletonRows }, (_, row) => (
            <tr key={row}>
              {columns.map((column) => (
                <td key={column}>
                  <Skeleton width={skeletonWidths[column]} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </LoadingRegion>
  )
}

/// Deliberately not "POST one to /tickets to get started", which was shell
/// copy in a user-facing state. It still names the ingest API, because there
/// is no new-ticket action in this interface and inventing one to make the
/// sentence friendlier would promise a screen that does not exist.
function EmptyQueue() {
  return (
    <div className="empty">
      <Inbox className="empty__icon" size={28} aria-hidden="true" focusable="false" />
      <p className="empty__title">The queue is empty.</p>
      <p className="empty__detail">
        Tickets arrive through the ingest API. None have been received yet.
      </p>
    </div>
  )
}

export function TicketQueue() {
  const { data, isPending, error } = useQueue()

  if (isPending) {
    return <QueueSkeleton />
  }

  if (error) {
    return <Alert>Could not load the queue: {error.message}</Alert>
  }

  if (data.length === 0) {
    return <EmptyQueue />
  }

  return (
    <table className="queue">
      <caption className="queue__caption">
        {data.length} {data.length === 1 ? 'ticket' : 'tickets'}
      </caption>
      <QueueHeader />
      <tbody>
        {data.map((ticket) => {
          const state = describeState(ticket)

          return (
            <tr key={ticket.id}>
              <td>
                <Link to={`/tickets/${ticket.id}`}>{ticket.subject}</Link>
              </td>
              <td>{ticket.category ?? <span className="muted">unclassified</span>}</td>
              <td>{ticket.priority ?? <span className="muted">—</span>}</td>
              <td>
                <span className={`state state--${state.tone}`}>
                  <StateIcon tone={state.tone} />
                  {state.label}
                </span>
                {ticket.failedGates.length > 0 && (
                  <span className="muted gates">{ticket.failedGates.map(describeGate).join(', ')}</span>
                )}
              </td>
              <td>{formatDate(ticket.createdAt)}</td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}
