import { describe, expect, it } from 'vitest'
import { describeGate, describeState } from './ticketState'

/// Which state a ticket is in, and the precedence between states, is tested
/// against PostgreSQL in TicketQueueTests. What is pinned here is how each
/// state reads: its words, and its tone, which picks the icon. The two
/// states that need a human share the blocked tone and its triangle.
describe('describeState', () => {
  it.each([
    ['Approved', 'Approved', 'reviewed'],
    ['ResolvedManually', 'Resolved manually', 'reviewed'],
    ['Rejected', 'Rejected', 'rejected'],
    ['DraftReady', 'Draft ready', 'ready'],
    ['DraftWithheld', 'Draft withheld', 'blocked'],
    ['ManualTriage', 'Manual triage', 'blocked'],
    ['ReadyToDraft', 'Ready to draft', 'ready'],
    ['NotTriaged', 'Not triaged', 'pending'],
  ])('shows %s as "%s"', (state, label, tone) => {
    expect(describeState(state)).toEqual({ label, tone })
  })

  it('falls back to the wire name for a state it does not know', () => {
    expect(describeState('SomethingNew')).toEqual({ label: 'SomethingNew', tone: 'pending' })
  })
})

describe('describeGate', () => {
  it('names a failed gate in words rather than showing a score', () => {
    expect(describeGate('RetrievalSimilarity')).toBe('nothing similar resolved')
    expect(describeGate('ClassificationScore')).toBe('classifier was unsure')
  })

  it('falls back to the wire name for a gate it does not know', () => {
    expect(describeGate('SomethingNew')).toBe('SomethingNew')
  })
})
