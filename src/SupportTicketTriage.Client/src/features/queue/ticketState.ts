export type TicketStateTone = 'reviewed' | 'rejected' | 'ready' | 'blocked' | 'pending'

export interface TicketState {
  label: string
  tone: TicketStateTone
}

/// What each queue state looks like. Which state a ticket is in is decided
/// on the server, in the queue query, so that a filter on state and the
/// column a reviewer reads cannot disagree.
const states: Record<string, TicketState> = {
  Approved: { label: 'Approved', tone: 'reviewed' },
  // Reuses the reviewed tone and its icon: a person decided and the ticket is
  // resolved, which is what that tone means. The word carries the difference.
  ResolvedManually: { label: 'Resolved manually', tone: 'reviewed' },
  Rejected: { label: 'Rejected', tone: 'rejected' },
  DraftReady: { label: 'Draft ready', tone: 'ready' },
  DraftWithheld: { label: 'Draft withheld', tone: 'blocked' },
  ManualTriage: { label: 'Manual triage', tone: 'blocked' },
  ReadyToDraft: { label: 'Ready to draft', tone: 'ready' },
  NotTriaged: { label: 'Not triaged', tone: 'pending' },
}

/// The states the filter offers, in the server's precedence order, which is
/// the order this map is written in.
export const queueStates = Object.keys(states)

/// A state this client does not know shows its wire name rather than
/// nothing, as describeGate does for a gate.
export function describeState(state: string): TicketState {
  return states[state] ?? { label: state, tone: 'pending' }
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
