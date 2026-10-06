import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  get,
  getOrNull,
  post,
  type SimilarTicket,
  type SubmitReview,
  type Ticket,
  type TicketClassification,
  type TicketDraft,
  type TicketQueueItem,
  type TicketReview,
  type TicketRouting,
} from './client'

/// Query keys in one place so that an invalidation after a review decision
/// cannot miss a view by misspelling its key.
///
/// The queue is ['tickets', 'queue'] rather than just ['tickets'] on purpose.
/// TanStack Query matches keys by prefix, so a bare ['tickets'] would be a
/// prefix of every per-ticket key and invalidating the queue would quietly
/// refetch all five queries of every ticket anyone had open. A ticket id is a
/// GUID, so it can never collide with the literal 'queue'.
export const keys = {
  queue: ['tickets', 'queue'] as const,
  ticket: (id: string) => ['tickets', id] as const,
  classification: (id: string) => ['tickets', id, 'classification'] as const,
  routing: (id: string) => ['tickets', id, 'routing'] as const,
  similar: (id: string) => ['tickets', id, 'similar'] as const,
  draft: (id: string) => ['tickets', id, 'draft'] as const,
  review: (id: string) => ['tickets', id, 'review'] as const,
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

/// The resources below answer 404 for a ticket that has not reached that
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

export function useReview(id: string) {
  return useQuery({
    queryKey: keys.review(id),
    queryFn: () => getOrNull<TicketReview>(`/tickets/${id}/review`),
  })
}

/// Generation is a reviewer action rather than part of ingest, because it is
/// the most expensive call in the system. It is idempotent per ticket, so
/// asking twice returns the draft that already exists.
export function useGenerateDraft(id: string) {
  const client = useQueryClient()

  return useMutation({
    mutationFn: () => post<TicketDraft>(`/tickets/${id}/draft`),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: keys.draft(id) })
      void client.invalidateQueries({ queryKey: keys.queue })
    },
  })
}

/// Approve, edit-and-approve and reject are all this one call; they differ by
/// what is sent, not by which endpoint is used.
export function useSubmitReview(id: string) {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (review: SubmitReview) => post<TicketReview>(`/tickets/${id}/review`, review),
    onSuccess: () => {
      // The whole ticket subtree, because approving resolves the ticket and
      // that changes what retrieval returns as well as the review itself.
      void client.invalidateQueries({ queryKey: keys.ticket(id) })
      void client.invalidateQueries({ queryKey: keys.queue })
    },
  })
}
