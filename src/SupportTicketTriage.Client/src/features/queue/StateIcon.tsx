import {
  CircleCheck,
  CircleDashed,
  CircleDot,
  CircleX,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import type { TicketStateTone } from './ticketState'

/**
 * A second encoding of the state word, for scanning a long queue.
 *
 * Deliberately redundant. The word stays and the colour stays; colour alone
 * fails a colour-blind reader and a glyph alone fails anyone who has not
 * learned the vocabulary. The icon is `aria-hidden` for the same reason -
 * it adds nothing a screen reader has not already been told.
 *
 * Four circles and one triangle, and the asymmetry is the whole point. Shape
 * is the only cue that survives at scanning speed down three hundred rows,
 * so exactly one state gets a different one: the one that needs a human.
 */
const icons: Record<TicketStateTone, LucideIcon> = {
  reviewed: CircleCheck,
  rejected: CircleX,
  ready: CircleDot,
  blocked: TriangleAlert,
  pending: CircleDashed,
}

export function StateIcon({ tone }: { tone: TicketStateTone }) {
  const Icon = icons[tone]

  return <Icon className="state__icon" size={15} aria-hidden="true" focusable="false" />
}
