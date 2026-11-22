import { describe, expect, it } from 'vitest'
import {
  axisLabelDays,
  countDelta,
  durationDelta,
  formatDuration,
  pointsDelta,
  readRange,
  share,
  singleArrivalDay,
} from './metrics'

describe('readRange', () => {
  it('accepts the three ranges and falls back to thirty days for anything else', () => {
    expect(readRange('7d')).toBe('7d')
    expect(readRange('90d')).toBe('90d')
    expect(readRange('1d')).toBe('30d')
    expect(readRange(null)).toBe('30d')
  })
})

describe('share', () => {
  /// "None out of none" is not 0%; it is no rate at all.
  it('is a whole percentage, or nothing when there is nothing to divide by', () => {
    expect(share(15, 62)).toBe(24)
    expect(share('1', '1')).toBe(100)
    expect(share(0, 0)).toBeNull()
  })
})

describe('deltas', () => {
  it('compares counts as a relative change, and refuses to when there was nothing before', () => {
    expect(countDelta(120, 100)).toBe('+20%')
    expect(countDelta(80, 100)).toBe('−20%')
    expect(countDelta(100, 100)).toBe('no change')
    expect(countDelta(300, 0)).toBe('—')
  })

  /// 20% to 25% is five points, not a 25% rise.
  it('compares shares in percentage points', () => {
    expect(pointsDelta(25, 20)).toBe('+5 pts')
    expect(pointsDelta(19, 20)).toBe('−1 pt')
    expect(pointsDelta(25, null)).toBe('—')
  })

  it('compares durations to the minute', () => {
    expect(durationDelta(3600, 4800)).toBe('−20 min')
    expect(durationDelta('7200', '3600')).toBe('+1 h')
    expect(durationDelta(3600, 3620)).toBe('no change')
    expect(durationDelta(3600, null)).toBe('—')
  })
})

describe('formatDuration', () => {
  it('uses the two largest units that matter, to the minute', () => {
    expect(formatDuration(20)).toBe('under a minute')
    expect(formatDuration(3189)).toBe('53 min')
    expect(formatDuration(3 * 3600)).toBe('3 h')
    expect(formatDuration(3 * 3600 + 40 * 60)).toBe('3 h 40 min')
    expect(formatDuration(52 * 3600)).toBe('2 d 4 h')
  })
})

describe('axisLabelDays', () => {
  /// Counted back from today, so every gap is the same and nothing can crowd
  /// the last label. Counting forward put the last two labels at days 24 and
  /// 29 of 30, five-sixths of a step apart.
  it('spaces about five labels evenly, always ending on the last day', () => {
    expect(axisLabelDays(30)).toEqual([5, 11, 17, 23, 29])
    expect(axisLabelDays(7)).toEqual([0, 2, 4, 6])
    expect(axisLabelDays(90)).toEqual([17, 35, 53, 71, 89])
  })

  it('copes with a series too short to space out', () => {
    expect(axisLabelDays(1)).toEqual([0])
    expect(axisLabelDays(0)).toEqual([])
  })
})

describe('singleArrivalDay', () => {
  it('names the day when every ticket arrived on it, and nothing otherwise', () => {
    const day = (d: string, received: number) => ({ day: d, received })

    expect(singleArrivalDay([day('2026-11-17', 0), day('2026-11-18', 300)])).toBe('2026-11-18')
    expect(singleArrivalDay([day('2026-11-17', 2), day('2026-11-18', 300)])).toBeNull()
    expect(singleArrivalDay([day('2026-11-17', 0), day('2026-11-18', 0)])).toBeNull()
  })
})
