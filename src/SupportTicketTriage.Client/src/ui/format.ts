/// Wire values as a reader sees them, shared so that the queue and the
/// dashboard can never describe the same thing two ways.

/// When something arrived, in the reader's own locale and zone: an arrival is
/// a moment the reader lives in, unlike the dashboard's UTC days.
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
}

const categoryLabels: Record<string, string> = {
  Billing: 'Billing',
  AccountAndLogin: 'Account and login',
  TechnicalFault: 'Technical fault',
  ShippingAndDelivery: 'Shipping and delivery',
  ReturnsAndRefunds: 'Returns and refunds',
  ProductQuestion: 'Product question',
}

/// A category as a person reads it. A category the client does not know yet
/// falls back to its wire name rather than showing nothing.
export function describeCategory(category: string): string {
  return categoryLabels[category] ?? category
}
