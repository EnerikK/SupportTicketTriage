import { toNumber } from '../../api/client'

/**
 * Two decimal places, everywhere a model-produced number reaches a reader.
 *
 * This existed three times and agreed twice. The routing panel rendered the
 * raw value, so a retrieval similarity arrived on screen as
 * `0.6972331811135415` directly above an evidence list showing `0.70` for the
 * same number. One of them had to be wrong, and the long one was: sixteen
 * digits of a float claims a precision the measurement does not have, and
 * invites a reader to compare two values that differ in the twelfth decimal.
 *
 * Deliberately **not** a percentage. `70% similar` reads as a calibrated
 * statement about how alike two tickets are; it is the cosine of the angle
 * between two embeddings, and ADR-003 forbids dressing either routing input
 * up as a probability.
 */
export function formatScore(value: number | string): string {
  return toNumber(value).toFixed(2)
}

export function similarityLabel(value: number | string): string {
  return `similarity ${formatScore(value)}`
}
