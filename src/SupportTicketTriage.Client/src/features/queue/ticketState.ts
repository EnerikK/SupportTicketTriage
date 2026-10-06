import type { TicketQueueItem } from '../../api/client'

export type TicketStateTone = 'reviewed' | 'rejected' | 'ready' | 'blocked' | 'pending'

export interface TicketState {
  label: string
  tone: TicketStateTone
}

/// Collapses the four independent triage signals the queue carries into the
/// one word a reviewer scans the column for.
///
/// Order matters and is the point: a decision already taken outranks the
/// draft that produced it, and a draft that failed citation validation is
/// called out rather than folded into "no draft", because ADR-010 withholds
/// its content and a reviewer needs to know why there is nothing to read.
export function describeState(item: TicketQueueItem): TicketState {
  if (item.reviewDecision === 'Approved') {
    return { label: 'Approved', tone: 'reviewed' }
  }

  if (item.reviewDecision === 'Rejected') {
    return { label: 'Rejected', tone: 'rejected' }
  }

  if (item.draftState === 'Ready') {
    return { label: 'Draft ready', tone: 'ready' }
  }

  if (item.draftState === 'Failed') {
    return { label: 'Draft withheld', tone: 'blocked' }
  }

  if (item.isDraftEligible === false) {
    return { label: 'Manual triage', tone: 'blocked' }
  }

  if (item.isDraftEligible === true) {
    return { label: 'Ready to draft', tone: 'ready' }
  }

  // No routing decision at all: the ticket was ingested but never evaluated,
  // which happens when it is still in flight.
  return { label: 'Not triaged', tone: 'pending' }
}

/// ADR-003 forbids presenting the routing inputs as a confidence percentage,
/// so a gated ticket says which gate failed, in words. "RetrievalSimilarity"
/// is the wire name; it is not what a reviewer should have to read.
const gateDescriptions: Record<string, string> = {
  ClassificationScore: 'classifier was unsure',
  RetrievalSimilarity: 'nothing similar resolved',
}

export function describeGate(gate: string): string {
  return gateDescriptions[gate] ?? gate
}
