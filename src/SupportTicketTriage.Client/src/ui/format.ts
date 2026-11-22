/// Shared so that the queue and the dashboard can never format the same
/// thing two ways. Category names are not formatted here: the API sends the
/// domain's own labels (TicketCategory.ToLabel), the one place they live.

/// When something arrived, in the reader's own locale and zone: an arrival is
/// a moment the reader lives in, unlike the dashboard's UTC days.
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}
