import { toNumber } from '../../api/client'
import { shareLabel } from './metrics'

export interface Bar {
  label: string
  count: number | string
  tone: 'ready' | 'blocked' | 'rejected' | 'accent' | 'muted'
}

/**
 * Rows of label, count and bar.
 *
 * A bar per row rather than one stacked bar: each row reads on its own, and
 * the colours only have to tell a few kinds of row apart, which the existing
 * state colours already do in both themes. A stacked bar would need a
 * distinct colour per segment, each also distinct in lightness.
 *
 * The bars are decoration and hidden from assistive technology; the counts
 * and shares are in the text.
 */
export function BarList({ bars, label, withShare = false }: { bars: Bar[]; label: string; withShare?: boolean }) {
  const counts = bars.map((bar) => toNumber(bar.count))
  const longest = Math.max(1, ...counts)
  const total = counts.reduce((sum, count) => sum + count, 0)

  return (
    <ul className="bars" aria-label={label}>
      {bars.map((bar, index) => (
        <li key={bar.label} className="bars__row">
          <span className="bars__label">{bar.label}</span>
          <span className="bars__count">{counts[index].toLocaleString()}</span>
          {withShare && <span className="bars__share">{shareLabel(counts[index], total)}</span>}
          <span className="bars__track" aria-hidden="true">
            <span
              className={`bars__fill bars__fill--${bar.tone}`}
              style={{ width: `${(counts[index] / longest) * 100}%` }}
            />
          </span>
        </li>
      ))}
    </ul>
  )
}
