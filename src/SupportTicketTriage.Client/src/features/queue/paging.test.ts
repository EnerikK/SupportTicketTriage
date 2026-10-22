import { describe, expect, it } from 'vitest'
import { defaultPageSize, describeRange, pageCount, readPage, readPageSize } from './paging'

describe('readPage', () => {
  it('reads a page number out of the query string', () => {
    expect(readPage('4')).toBe(4)
  })

  /// All of these are reachable by typing in the address bar, and none of
  /// them may render a broken queue.
  it.each([null, '', 'abc', '0', '-3', '2.5', 'NaN'])('falls back to one for %s', (value) => {
    expect(readPage(value)).toBe(1)
  })
})

describe('readPageSize', () => {
  it.each([10, 25, 50, 100])('accepts the offered size %i', (size) => {
    expect(readPageSize(String(size))).toBe(size)
  })

  /// Snapped to the offered set rather than clamped. 37 is not a size this
  /// interface has a control for, so honouring it would leave the dropdown
  /// showing something that does not match what is on screen.
  it.each(['37', '0', '1000', 'abc', null])('falls back to the default for %s', (value) => {
    expect(readPageSize(value)).toBe(defaultPageSize)
  })
})

describe('pageCount', () => {
  it('rounds a partial last page up', () => {
    expect(pageCount(301, 10)).toBe(31)
  })

  it('is exact when the last page is full', () => {
    expect(pageCount(300, 10)).toBe(30)
  })

  /// "Page 1 of 0" is not a thing to show anybody.
  it('is at least one for an empty queue', () => {
    expect(pageCount(0, 10)).toBe(1)
  })
})

describe('describeRange', () => {
  it('describes a full page', () => {
    expect(describeRange(2, 10, 301)).toBe('Showing 11–20 of 301')
  })

  /// The last page is almost never full. Without the clamp this claims to
  /// be showing up to 310 of 301.
  it('stops the last page at the total', () => {
    expect(describeRange(31, 10, 301)).toBe('Showing 301–301 of 301')
  })

  it('describes a single-page queue', () => {
    expect(describeRange(1, 10, 3)).toBe('Showing 1–3 of 3')
  })
})
