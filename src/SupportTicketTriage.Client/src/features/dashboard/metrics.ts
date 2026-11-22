import { toNumber } from '../../api/client'

/// The API serves counts and leaves every share, delta and rounding decision
/// to this file, so each one is made in exactly one place (ADR-046).

export const ranges = ['7d', '30d', '90d'] as const
export type DashboardRange = (typeof ranges)[number]
export const defaultRange: DashboardRange = '30d'

const rangeDays: Record<DashboardRange, number> = { '7d': 7, '30d': 30, '90d': 90 }

export function rangeLabel(range: DashboardRange): string {
  return `${rangeDays[range]} days`
}

/// The range in the address, or the default for anything else. A hand-typed
/// `?range=1d` shows the default rather than an error page.
export function readRange(value: string | null): DashboardRange {
  return (ranges as readonly string[]).includes(value ?? '') ? (value as DashboardRange) : defaultRange
}

type Count = number | string

/// A whole-number share, or null when there is nothing to divide by. Null
/// rather than 0%, because "none out of none" is not a rate.
export function share(part: Count, whole: Count): number | null {
  const denominator = toNumber(whole)
  return denominator > 0 ? Math.round((toNumber(part) / denominator) * 100) : null
}

/// A share as a reader should see it: a whole percentage, except that a
/// non-zero part never reads as 0% and a part short of the whole never reads
/// as 100%, because each would claim something false. Nothing to divide by
/// reads as "—".
export function shareLabel(part: Count, whole: Count): string {
  const denominator = toNumber(whole)
  if (denominator <= 0) {
    return '—'
  }

  const numerator = toNumber(part)
  const rounded = Math.round((numerator / denominator) * 100)

  if (numerator > 0 && rounded === 0) {
    return '<1%'
  }

  if (numerator < denominator && rounded === 100) {
    return '>99%'
  }

  return `${rounded}%`
}

const noComparison = '—'

function signed(value: number, unit: string): string {
  if (value === 0) {
    return 'no change'
  }

  // U+2212, a real minus sign, so the digits line up with the plus.
  return `${value > 0 ? '+' : '−'}${Math.abs(value)}${unit}`
}

/// Relative change in a count. With nothing in the previous period there is
/// no honest percentage, so it says so instead of inventing an infinite one.
export function countDelta(current: Count, previous: Count): string {
  const before = toNumber(previous)
  if (before === 0) {
    return noComparison
  }

  return signed(Math.round(((toNumber(current) - before) / before) * 100), '%')
}

/// Change in a share, in percentage points: 20% to 25% is +5 pts, not +25%.
export function pointsDelta(current: number | null, previous: number | null): string {
  if (current === null || previous === null) {
    return noComparison
  }

  const difference = current - previous
  return signed(difference, Math.abs(difference) === 1 ? ' pt' : ' pts')
}

export function durationDelta(
  current: Count | null | undefined,
  previous: Count | null | undefined,
): string {
  if (current == null || previous == null) {
    return noComparison
  }

  const difference = toNumber(current) - toNumber(previous)
  if (Math.round(difference / 60) === 0) {
    return 'no change'
  }

  return `${difference > 0 ? '+' : '−'}${formatDuration(Math.abs(difference))}`
}

/// To the minute, in the largest two units that matter. Seconds would claim
/// a precision a median over a handful of tickets does not have.
export function formatDuration(seconds: number): string {
  const minutes = Math.round(seconds / 60)
  if (minutes < 1) {
    return 'under a minute'
  }

  if (minutes < 60) {
    return `${minutes} min`
  }

  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    const rest = minutes % 60
    return rest ? `${hours} h ${rest} min` : `${hours} h`
  }

  const days = Math.floor(hours / 24)
  const restHours = hours % 24
  return restHours ? `${days} d ${restHours} h` : `${days} d`
}

/// The one day every ticket in the period arrived on, when there is exactly
/// one. ADR-046 commits the page to saying so, rather than drawing a single
/// spike as though it were a trend.
export function singleArrivalDay(volume: { day: string; received: Count }[]): string | null {
  const busy = volume.filter((day) => toNumber(day.received) > 0)
  return busy.length === 1 ? busy[0].day : null
}

/// Which days the chart's axis labels: about five, evenly spaced, counted back
/// from today, so the last day is always labelled and no label can crowd it.
/// Counting forward from the first day instead left the last two labels
/// five-sixths of a step apart at 30 days, which overlapped at phone width.
export function axisLabelDays(count: number, target = 5): number[] {
  if (count <= 0) {
    return []
  }

  const step = Math.max(1, Math.ceil(count / target))
  const indices: number[] = []
  for (let index = count - 1; index >= 0; index -= step) {
    indices.unshift(index)
  }

  return indices
}

/// A UTC calendar day as a short date. Formatted in UTC because that is what
/// the day is; formatting it in the viewer's zone could show the day before.
export function formatDay(day: string): string {
  return new Date(`${day}T00:00:00Z`).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    timeZone: 'UTC',
  })
}
