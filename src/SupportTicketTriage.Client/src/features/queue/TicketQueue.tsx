import { Link } from 'react-router'
import { useQueue } from '../../api/queries'
import { describeGate, describeState } from './ticketState'

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

export function TicketQueue() {
  const { data, isPending, error } = useQueue()

  if (isPending) {
    return <p className="status">Loading the queue…</p>
  }

  if (error) {
    return (
      <p className="status status--error" role="alert">
        Could not load the queue: {error.message}
      </p>
    )
  }

  if (data.length === 0) {
    return <p className="status">No tickets yet. POST one to /tickets to get started.</p>
  }

  return (
    <table className="queue">
      <caption className="queue__caption">
        {data.length} {data.length === 1 ? 'ticket' : 'tickets'}
      </caption>
      <thead>
        <tr>
          <th scope="col">Subject</th>
          <th scope="col">Category</th>
          <th scope="col">Priority</th>
          <th scope="col">State</th>
          <th scope="col">Received</th>
        </tr>
      </thead>
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
                <span className={`state state--${state.tone}`}>{state.label}</span>
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
