import type { components } from './schema'

type Schemas = components['schemas']

export type TicketQueueItem = Schemas['TicketQueueItemResponse']
export type Ticket = Schemas['TicketResponse']
export type TicketClassification = Schemas['TicketClassificationResponse']
export type TicketRouting = Schemas['TicketRoutingResponse']
export type SimilarTicket = Schemas['SimilarTicketResponse']
export type TicketDraft = Schemas['TicketDraftResponse']
export type TicketReview = Schemas['TicketReviewResponse']
export type SubmitReview = Schemas['SubmitReviewRequest']

/// Every type above is generated from the API's own OpenAPI document by
/// `npm run generate-types`. Nothing here is hand-written, so a renamed field
/// on the server becomes a compile error here rather than `undefined` at
/// runtime.

export class ApiError extends Error {
  // Declared and assigned rather than written as a constructor parameter
  // property: the tsconfig sets `erasableSyntaxOnly`, so TypeScript may only
  // add types that vanish at build time, never syntax that emits runtime code.
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function send(path: string, init?: RequestInit): Promise<Response> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json', ...init?.headers } : init?.headers,
  })

  return response
}

async function failureMessage(response: Response): Promise<string> {
  // The API answers a refused action with a plain-text conflict message and a
  // broken submission with a ProblemDetails body. Both carry the reason, and
  // the reason is what a reviewer needs to see.
  const text = await response.text()
  if (!text) {
    return response.statusText
  }

  try {
    const problem = JSON.parse(text) as {
      detail?: string
      title?: string
      errors?: Record<string, string[]>
    }

    const fromErrors = problem.errors && Object.values(problem.errors).flat().join(' ')
    return fromErrors || problem.detail || problem.title || text
  } catch {
    return text
  }
}

export async function get<T>(path: string): Promise<T> {
  const response = await send(path)

  if (!response.ok) {
    throw new ApiError(response.status, await failureMessage(response))
  }

  return (await response.json()) as T
}

/// A 404 is a real answer on several of these resources, not a failure: a
/// ticket that has never been classified, routed or drafted returns one by
/// design, so that "absent" and "present" are never confused. Callers that
/// expect absence use this and branch on null; callers that do not use `get`
/// and let the error surface.
export async function getOrNull<T>(path: string): Promise<T | null> {
  const response = await send(path)

  if (response.status === 404) {
    return null
  }

  if (!response.ok) {
    throw new ApiError(response.status, await failureMessage(response))
  }

  return (await response.json()) as T
}

export async function post<T>(path: string, body?: unknown): Promise<T> {
  const response = await send(path, {
    method: 'POST',
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    throw new ApiError(response.status, await failureMessage(response))
  }

  return (await response.json()) as T
}
