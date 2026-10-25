import { describeRange, pageCount, pageSizes } from './paging'

interface Props {
  page: number
  pageSize: number
  totalCount: number
  onPage: (page: number) => void
  onPageSize: (pageSize: number) => void
}

/// The queue's pager.
///
/// Deliberately text-only. ADR-025's rule is that icons go beside buttons
/// and never inside them, and a chevron inside "Previous" would be the first
/// exception — bought for decoration, on controls whose words already say
/// exactly what they do.
///
/// `<nav>` with a name because this is navigation between pages of one
/// resource, and a screen reader user landing on it should be told what the
/// buttons move through.
export function Pagination({ page, pageSize, totalCount, onPage, onPageSize }: Props) {
  const pages = pageCount(totalCount, pageSize)

  return (
    <nav className="pagination" aria-label="Queue pages">
      <label className="pagination__size">
        <span className="pagination__label">Per page</span>
        <select
          value={pageSize}
          onChange={(event) => onPageSize(Number(event.target.value))}
        >
          {pageSizes.map((size) => (
            <option key={size} value={size}>
              {size}
            </option>
          ))}
        </select>
      </label>

      <p className="pagination__range">{describeRange(page, pageSize, totalCount)}</p>

      <div className="pagination__controls">
        <button type="button" onClick={() => onPage(page - 1)} disabled={page <= 1}>
          Previous
        </button>

        {/* aria-live, because paging replaces the table under the reader
            without moving focus. Without it the only announcement of having
            moved is silence. */}
        <p className="pagination__position" aria-live="polite">
          Page {page} of {pages}
        </p>

        <button type="button" onClick={() => onPage(page + 1)} disabled={page >= pages}>
          Next
        </button>
      </div>
    </nav>
  )
}
