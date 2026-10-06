import { useQuery } from '@tanstack/react-query'
import {
  get,
  getOrNull,
  type SimilarTicket,
  type Ticket,
  type TicketClassification,
  type TicketDraft,
  type TicketQueueItem,
  type TicketRouting,
} from './client'

/// Query keys in one place so that an invalidation after a review decision
/// cannot miss a view by misspelling its key. Kept deliberately flat: this
/// app has a queue and a ticket, and a key factory with more structure than
/// that would be machinery without a use.
export const keys = {
  queue: ['tickets'] as const,
  ticket: (id: string) => ['tickets', id] as const,
  classification: (id: string) => ['tickets', id, 'classification'] as const,
  routing: (id: string) => ['tickets', id, 'routing'] as const,
  similar: (id: string) => ['tickets', id, 'similar'] as const,
  draft: (id: string) => ['tickets', id, 'draft'] as const,
}

export function useQueue() {
  return useQuery({
    queryKey: keys.queue,
    queryFn: () => get<TicketQueueItem[]>('/tickets'),
  })
}

export function useTicket(id: string) {
  return useQuery({
    queryKey: keys.ticket(id),
    queryFn: () => getOrNull<Ticket>(`/tickets/${id}`),
  })
}

/// The four resources below answer 404 for a ticket that has not reached that
/// stage yet, which is a state rather than a failure - so they resolve to null
/// and the view renders "not yet" instead of an error. `getOrNull` is what
/// draws that line; anything other than a 404 still throws.

export function useClassification(id: string) {
  return useQuery({
    queryKey: keys.classification(id),
    queryFn: () => getOrNull<TicketClassification>(`/tickets/${id}/classification`),
  })
}

export function useRouting(id: string) {
  return useQuery({
    queryKey: keys.routing(id),
    queryFn: () => getOrNull<TicketRouting>(`/tickets/${id}/routing`),
  })
}

export function useSimilar(id: string) {
  return useQuery({
    queryKey: keys.similar(id),
    queryFn: () => get<SimilarTicket[]>(`/tickets/${id}/similar`),
  })
}

export function useDraft(id: string) {
  return useQuery({
    queryKey: keys.draft(id),
    queryFn: () => getOrNull<TicketDraft>(`/tickets/${id}/draft`),
  })
}
