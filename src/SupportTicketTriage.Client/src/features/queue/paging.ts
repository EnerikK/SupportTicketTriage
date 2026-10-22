/// The page sizes offered, and the one used when nothing says otherwise.
///
/// The API clamps to a ceiling of 100 independently; this list is the set a
/// reviewer can pick from, not the enforcement. A client that offered 500
/// would simply be told it got 100.
export const pageSizes = [10, 25, 50, 100] as const
export const defaultPageSize = 10

/// Paging state lives in the URL, so both of these parse text a user can
/// type. `?page=abc`, `?page=-3` and `?pageSize=37` are all reachable by
/// hand and none of them may render a broken queue.
export function readPage(value: string | null): number {
  const parsed = Number(value)

  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1
}

/// Unlike the page number this snaps to the offered set rather than merely
/// clamping. A size of 37 is not a page size this interface has a control
/// for, and silently honouring it would leave the dropdown showing something
/// that does not match what is on screen.
export function readPageSize(value: string | null): number {
  const parsed = Number(value)

  return (pageSizes as readonly number[]).includes(parsed) ? parsed : defaultPageSize
}

/// At least one, so an empty queue reads "Page 1 of 1" rather than "of 0".
export function pageCount(totalCount: number, pageSize: number): number {
  return Math.max(1, Math.ceil(totalCount / pageSize))
}

/// "Showing 11–20 of 301".
///
/// The last page is almost never full, so the upper bound is the smaller of
/// the page's end and the total — otherwise the final page of 301 claims to
/// be showing up to 310.
export function describeRange(page: number, pageSize: number, totalCount: number): string {
  const first = (page - 1) * pageSize + 1
  const last = Math.min(page * pageSize, totalCount)

  return `Showing ${first}–${last} of ${totalCount}`
}
