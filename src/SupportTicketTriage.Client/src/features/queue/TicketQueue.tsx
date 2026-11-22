import { Inbox } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { toNumber } from '../../api/client'
import { useCategories, useQueue, type QueueFilters } from '../../api/queries'
import { Alert } from '../../ui/Alert'
import { formatDate } from '../../ui/format'
import { LoadingRegion, Skeleton } from '../../ui/Skeleton'
import { Pagination } from './Pagination'
import { readPage, readPageSize } from './paging'
import { StateIcon } from './StateIcon'
import { isFiltered, priorities, readFilter } from './filters'
import { describeGate, describeState, queueStates } from './ticketState'

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

/// The colgroup gives each column its width from the stylesheet, so the
/// table and its skeleton share one set of columns and paging cannot
/// re-measure them from a different page's content.
function QueueHeader() {
  return (
    <>
      <colgroup>
        {columns.map((column) => (
          <col key={column} className={`queue__col--${column.toLowerCase()}`} />
        ))}
      </colgroup>
      <thead>
        <tr>
          {columns.map((column) => (
            <th key={column} scope="col">
              {column}
            </th>
          ))}
        </tr>
      </thead>
    </>
  )
}

/// Rows follow the page size, so the skeleton is the shape of the page that
/// is actually coming rather than a fixed eight that would resize under the
/// reader on anything but the default.
function QueueSkeleton({ rows }: { rows: number }) {
  return (
    <LoadingRegion label="Loading the queue">
      {/* The pager sits above the table, so the skeleton holds its row too,
          or the table would drop by that height when the data arrives. The
          strut takes the button class, so the row is the buttons' height by
          construction rather than by a measured number. */}
      <div className="pagination">
        <Skeleton width="14rem" />
        <span className="button pagination__strut">{' '}</span>
      </div>
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

/// A filter that matches nothing is not an empty queue. "The queue is
/// empty" would claim there is no work when there may be three hundred
/// tickets behind the filter. The way out is the Clear filters button above,
/// which is why this does not offer a second one.
function NoMatches() {
  return (
    <div className="empty">
      <p className="empty__title">No tickets match these filters.</p>
      <p className="empty__detail">Clear the filters to see the whole queue.</p>
    </div>
  )
}

/// A value in the table that filters the queue to itself: a pointer
/// shortcut for the dropdowns above, which do the same by keyboard.
///
/// Out of the tab order (tabIndex -1), or every row would cost three Tab
/// presses instead of one. Still a real button, so a screen reader finds and
/// activates it. The name contains the visible word (WCAG 2.5.3), and the
/// title shows a pointer user what a click will do.
///
/// Where the column is already filtered to the value, the caller renders
/// plain text instead: every row of a filtered table shows that value, and a
/// button that does nothing on every line helps nobody.
function FilterValue({
  name,
  onClick,
  children,
}: {
  name: string
  onClick: () => void
  children: ReactNode
}) {
  return (
    <button
      type="button"
      className="filter-value"
      tabIndex={-1}
      aria-label={name}
      title={name}
      onClick={onClick}
    >
      {children}
    </button>
  )
}

interface QueueUpdate {
  page?: number
  pageSize?: number
  priority?: string | null
  state?: string | null
  category?: string | null
}

/// Native selects with visible labels, each defaulting to "Any", in the
/// order of the table's columns.
function FilterBar({
  filters,
  categories,
  categoriesFailed,
  onChange,
}: {
  filters: QueueFilters
  categories: readonly string[]
  categoriesFailed: boolean
  onChange: (next: QueueUpdate) => void
}) {
  return (
    <div className="queue-filters" role="group" aria-label="Filter the queue">
      <label className="queue-filters__field">
        <span className="queue-filters__label">Category</span>
        <select
          value={filters.category ?? ''}
          disabled={categories.length === 0}
          onChange={(event) => onChange({ category: event.target.value || null })}
        >
          <option value="">Any</option>
          {categories.map((category) => (
            <option key={category} value={category}>
              {category}
            </option>
          ))}
        </select>
      </label>

      {/* Named rather than left as a dropdown that silently offers nothing. */}
      {categoriesFailed && <span className="muted">Categories could not be loaded.</span>}

      <label className="queue-filters__field">
        <span className="queue-filters__label">Priority</span>
        <select
          value={filters.priority ?? ''}
          onChange={(event) => onChange({ priority: event.target.value || null })}
        >
          <option value="">Any</option>
          {priorities.map((priority) => (
            <option key={priority} value={priority}>
              {priority}
            </option>
          ))}
        </select>
      </label>

      <label className="queue-filters__field">
        <span className="queue-filters__label">State</span>
        <select
          value={filters.state ?? ''}
          onChange={(event) => onChange({ state: event.target.value || null })}
        >
          <option value="">Any</option>
          {queueStates.map((state) => (
            <option key={state} value={state}>
              {describeState(state).label}
            </option>
          ))}
        </select>
      </label>

      {/* Hidden rather than removed while no filter is set, so setting the
          first one does not push the pager and the table down. `visibility:
          hidden` also takes it out of the tab order and the accessibility
          tree, so nobody can reach a button that would do nothing. */}
      <button
        type="button"
        style={{ visibility: isFiltered(filters) ? 'visible' : 'hidden' }}
        onClick={() => onChange({ priority: null, state: null, category: null })}
      >
        Clear filters
      </button>
    </div>
  )
}

export function TicketQueue() {
  // Paging and filters live in the URL rather than in component state: the
  // back button works, a filtered page is a link somebody can send, and a
  // refresh does not throw a reviewer back to the top of a queue they were
  // working through. ADR-015 already put navigation in react-router rather
  // than in selection state, and this is the same call.
  const [params, setParams] = useSearchParams()

  const page = readPage(params.get('page'))
  const pageSize = readPageSize(params.get('pageSize'))
  const categories = useCategories()
  const requestedCategory = params.get('category')

  const filters: QueueFilters = {
    priority: readFilter(params.get('priority'), priorities),
    state: readFilter(params.get('state'), queueStates),
    category: readFilter(requestedCategory, categories.data ?? []),
  }

  // A category in the address can only be checked against the list once the
  // list has arrived, so until then the queue waits rather than loading once
  // unfiltered and again filtered. If the list fails, the category reads as
  // "Any" and the queue loads; the filter bar says the list is missing.
  const ready = !requestedCategory || !categories.isPending

  function update(next: QueueUpdate) {
    const updated = new URLSearchParams(params)

    for (const key of ['priority', 'state', 'category'] as const) {
      const value = next[key]

      if (value !== undefined) {
        if (value) {
          updated.set(key, value)
        } else {
          updated.delete(key)
        }

        // A filter changes which tickets there are, so the page number no
        // longer refers to anything. The rule page size already follows.
        updated.delete('page')
      }
    }

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

  // The filter bar renders whatever the results are doing - loading, failed,
  // empty - so it never moves, and it is there to undo a filter that
  // matched nothing.
  return (
    <>
      <FilterBar
        filters={filters}
        categories={categories.data ?? []}
        categoriesFailed={categories.isError}
        onChange={update}
      />
      <QueueResults
        page={page}
        pageSize={pageSize}
        filters={filters}
        ready={ready}
        update={update}
      />
    </>
  )
}

function QueueResults({
  page,
  pageSize,
  filters,
  ready,
  update,
}: {
  page: number
  pageSize: number
  filters: QueueFilters
  ready: boolean
  update: (next: QueueUpdate) => void
}) {
  // A query that is not enabled yet reports itself as pending, so the
  // skeleton covers the wait for the category list as well.
  const { data, isPending, error } = useQueue(page, pageSize, filters, ready)
  const filtered = isFiltered(filters)

  if (isPending) {
    return <QueueSkeleton rows={pageSize} />
  }

  if (error) {
    return <Alert>Could not load the queue: {error.message}</Alert>
  }

  const totalCount = toNumber(data.totalCount)
  const tickets = totalCount === 1 ? 'ticket' : 'tickets'

  if (totalCount === 0) {
    return filtered ? <NoMatches /> : <EmptyQueue />
  }

  // Reachable by typing a page number past the end. The API answers it with
  // an empty page and the true total rather than silently clamping, so the
  // interface says what happened instead of pretending page 400 was page 31.
  if (data.items.length === 0) {
    return (
      <div className="empty">
        <p className="empty__title">There is nothing on page {page}.</p>
        <p className="empty__detail">
          {filtered
            ? `${totalCount} ${tickets} match these filters.`
            : `The queue holds ${totalCount} ${tickets}.`}{' '}
          <button type="button" className="link-button" onClick={() => update({ page: 1 })}>
            Back to the first page
          </button>
        </p>
      </div>
    )
  }

  return (
    <>
    {/* Above the table, not below it. The table's height follows its rows -
        a gate line makes a row taller, the last page is short - so a pager
        beneath it moved under the cursor on every page turn. Nothing above
        this depends on the page's content. */}
    <Pagination
      page={page}
      pageSize={pageSize}
      totalCount={totalCount}
      onPage={(next) => update({ page: next })}
      onPageSize={(next) => update({ pageSize: next })}
    />

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
          {totalCount} {filtered ? `matching ${tickets}` : tickets}
        </caption>
        <QueueHeader />
        <tbody>
          {data.items.map((ticket) => {
            const state = describeState(ticket.state)

            return (
              <tr key={ticket.id}>
                <td>
                  <Link to={`/tickets/${ticket.id}`}>{ticket.subject}</Link>
                </td>
                <td>{ticket.category ?? <span className="muted">unclassified</span>}</td>
                <td>
                  {ticket.priority === null ? (
                    <span className="muted">—</span>
                  ) : ticket.priority === filters.priority ? (
                    ticket.priority
                  ) : (
                    <FilterValue
                      name={`Show only ${ticket.priority} priority`}
                      onClick={() => update({ priority: ticket.priority })}
                    >
                      {ticket.priority}
                    </FilterValue>
                  )}
                </td>
                <td>
                  {/* The icon stays beside the button, never inside it
                      (ADR-025), and the gate line stays outside it, so the
                      button's name is the state alone. */}
                  <span className={`state state--${state.tone}`}>
                    <StateIcon tone={state.tone} />
                    {ticket.state === filters.state || !queueStates.includes(ticket.state) ? (
                      state.label
                    ) : (
                      <FilterValue
                        name={`Show only ${state.label}`}
                        onClick={() => update({ state: ticket.state })}
                      >
                        {state.label}
                      </FilterValue>
                    )}
                  </span>
                  {ticket.failedGates.length > 0 && (
                    <span className="muted gates">
                      {ticket.failedGates.map(describeGate).join(', ')}
                    </span>
                  )}
                </td>
                <td className="queue__date">{formatDate(ticket.createdAt)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
    </>
  )
}
