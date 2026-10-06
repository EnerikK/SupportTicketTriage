import { describe, expect, it } from 'vitest'
import type { TicketQueueItem } from '../../api/client'
import { describeGate, describeState } from './ticketState'

function item(overrides: Partial<TicketQueueItem> = {}): TicketQueueItem {
  return {
    id: '01a110da-f73f-7e2f-b69f-53d2ca38bc34',
    subject: 'Charged twice',
    createdAt: '2026-10-06T10:55:48Z',
    category: null,
    priority: null,
    isDraftEligible: null,
    failedGates: [],
    draftState: 'None',
    reviewDecision: null,
    isResolved: false,
    ...overrides,
  }
}

describe('describeState', () => {
  it('reports a ticket that was never routed as untriaged', () => {
    expect(describeState(item())).toEqual({ label: 'Not triaged', tone: 'pending' })
  })

  it('reports a gated ticket as manual triage', () => {
    const state = describeState(
      item({ isDraftEligible: false, failedGates: ['ClassificationScore'] }),
    )

    expect(state).toEqual({ label: 'Manual triage', tone: 'blocked' })
  })

  it('reports an eligible ticket with no draft yet as ready to draft', () => {
    expect(describeState(item({ isDraftEligible: true }))).toEqual({
      label: 'Ready to draft',
      tone: 'ready',
    })
  })

  it('reports a generated draft as ready', () => {
    expect(describeState(item({ isDraftEligible: true, draftState: 'Ready' }))).toEqual({
      label: 'Draft ready',
      tone: 'ready',
    })
  })

  /// A draft that cited evidence it was never shown is withheld by the API.
  /// The queue has to say so rather than look like no draft was attempted,
  /// because the two mean entirely different things to whoever is on call.
  it('distinguishes a withheld draft from no draft at all', () => {
    const withheld = describeState(item({ isDraftEligible: true, draftState: 'Failed' }))
    const none = describeState(item({ isDraftEligible: true, draftState: 'None' }))

    expect(withheld.label).toBe('Draft withheld')
    expect(none.label).not.toBe(withheld.label)
  })

  /// A decision a person already took outranks the draft that produced it.
  it('prefers a recorded review decision over the draft state', () => {
    expect(
      describeState(item({ draftState: 'Ready', reviewDecision: 'Approved' })).label,
    ).toBe('Approved')

    expect(
      describeState(item({ draftState: 'Ready', reviewDecision: 'Rejected' })).label,
    ).toBe('Rejected')
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
