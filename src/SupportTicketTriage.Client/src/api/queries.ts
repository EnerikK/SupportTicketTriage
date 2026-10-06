import { useQuery } from '@tanstack/react-query'
import { get, type TicketQueueItem } from './client'

/// Query keys in one place so that an invalidation after a review decision
/// cannot miss a view by misspelling its key. Kept deliberately flat: this
/// app has a queue and a ticket, and a key factory with more structure than
/// that would be machinery without a use.
export const keys = {
  queue: ['tickets'] as const,
  ticket: (id: string) => ['tickets', id] as const,
}

export function useQueue() {
  return useQuery({
    queryKey: keys.queue,
    queryFn: () => get<TicketQueueItem[]>('/tickets'),
  })
}
