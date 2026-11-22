import type { QueueFilters } from '../../api/queries'

/// The priorities a reviewer can filter by: the domain's three. A copy, where
/// category labels come from the API, because these are three member names
/// that have not changed since classification was built. If they drift, the
/// API answers a stale value with a 400 that the page shows, not a silent
/// empty queue.
export const priorities = ['Low', 'Normal', 'High'] as const

/// Filters live in the URL beside page and pageSize, so this parses text a
/// user can type. A value not on offer reads as "Any", the way readPageSize
/// snaps to the offered sizes: a dropdown showing "Any" over a filtered
/// table, or the reverse, would misreport what is on screen. A different
/// case reads as the value it spells.
export function readFilter(value: string | null, offered: readonly string[]): string | null {
  if (!value) {
    return null
  }

  return offered.find((option) => option.toLowerCase() === value.toLowerCase()) ?? null
}

export function isFiltered(filters: QueueFilters): boolean {
  return filters.priority !== null || filters.state !== null || filters.category !== null
}
