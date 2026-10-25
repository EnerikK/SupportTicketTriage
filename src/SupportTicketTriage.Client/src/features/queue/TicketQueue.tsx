import { Inbox } from 'lucide-react'
import { Link, useSearchParams } from 'react-router'
import { toNumber } from '../../api/client'
import { useQueue } from '../../api/queries'
import { Alert } from '../../ui/Alert'
import { LoadingRegion, Skeleton } from '../../ui/Skeleton'
import { Pagination } from './Pagination'
import { readPage, readPageSize } from './paging'
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

/// Rows follow the page size, so the skeleton is the shape of the page that
/// is actually coming rather than a fixed eight that would resize under the
/// reader on anything but the default.
function QueueSkeleton({ rows }: { rows: number }) {
  return (
    <LoadingRegion label="Loading the queue">
      {/* The same scroll wrapper, deliberately without `tabIndex` or the
          region role: this sits inside an `aria-hidden` subtree, and a
          focusable element hidden from assistive technology is a trap a
          keyboard can reach but a screen reader cannot describe. */}
      <div className="queue-scroll">
      <table className="queue">
        <caption className="queue__caption">
          <Skeleton width="5rem" />
        </caption>
        <QueueHeader />
        <tbody>
          {Array.from({ length: rows }, (_, row) => (
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
      </div>
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
  // Paging lives in the URL rather than in component state: the back button
  // works, a page is a link somebody can send, and a refresh does not throw
  // a reviewer back to the top of a queue they were working through. ADR-015
  // already put navigation in react-router rather than in selection state,
  // and this is the same call.
  const [params, setParams] = useSearchParams()

  const page = readPage(params.get('page'))
  const pageSize = readPageSize(params.get('pageSize'))

  const { data, isPending, error } = useQueue(page, pageSize)

  function update(next: { page?: number; pageSize?: number }) {
    const updated = new URLSearchParams(params)

    if (next.pageSize !== undefined) {
      updated.set('pageSize', String(next.pageSize))
      // Page size changing resets to the first page. Holding page 9 while
      // moving from 10 rows to 100 lands the reviewer somewhere they never
      // asked to be, and on a short queue it lands them past the end.
      updated.delete('page')
    }

    if (next.page !== undefined) {
      // Page one is the absence of the parameter, so it has exactly one
      // URL rather than two that render identically.
      if (next.page <= 1) {
        updated.delete('page')
      } else {
        updated.set('page', String(next.page))
      }
    }

    setParams(updated)
  }

  if (isPending) {
    return <QueueSkeleton rows={pageSize} />
  }

  if (error) {
    return <Alert>Could not load the queue: {error.message}</Alert>
  }

  const totalCount = toNumber(data.totalCount)

  if (totalCount === 0) {
    return <EmptyQueue />
  }

  // Reachable by typing a page number past the end. The API answers it with
  // an empty page and the true total rather than silently clamping, so the
  // interface says what happened instead of pretending page 400 was page 31.
  if (data.items.length === 0) {
    return (
      <div className="empty">
        <p className="empty__title">There is nothing on page {page}.</p>
        <p className="empty__detail">
          The queue holds {totalCount} {totalCount === 1 ? 'ticket' : 'tickets'}.{' '}
          <button type="button" className="link-button" onClick={() => update({ page: 1 })}>
            Back to the first page
          </button>
        </p>
      </div>
    )
  }

  return (
    <>
    {/* A scrollable region has to be reachable by keyboard, or the content
        past its right edge is unreachable without a mouse - WCAG 2.1.1. That
        is what `tabIndex` and the labelled `region` role are for, not
        decoration.

        The cost, stated honestly: on a wide screen the table does not
        overflow and this is a tab stop that leads nowhere. Removing it only
        when it does not scroll needs a ResizeObserver, and a stray tab stop
        is a smaller fault than content a keyboard cannot reach. */}
    <div className="queue-scroll" tabIndex={0} role="region" aria-label="Ticket queue">
      <table className="queue">
        <caption className="queue__caption">
          {totalCount} {totalCount === 1 ? 'ticket' : 'tickets'}
        </caption>
        <QueueHeader />
        <tbody>
          {data.items.map((ticket) => {
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
                    <span className="muted gates">
                      {ticket.failedGates.map(describeGate).join(', ')}
                    </span>
                  )}
                </td>
                <td>{formatDate(ticket.createdAt)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>

    <Pagination
      page={page}
      pageSize={pageSize}
      totalCount={totalCount}
      onPage={(next) => update({ page: next })}
      onPageSize={(next) => update({ pageSize: next })}
    />
    </>
  )
}
