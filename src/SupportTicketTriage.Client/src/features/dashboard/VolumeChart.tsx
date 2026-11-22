import { toNumber, type DashboardDay } from '../../api/client'
import { axisLabelDays, formatDay } from './metrics'

const WIDTH = 1000
const HEIGHT = 240

/// A round number just above the largest value, so the top gridline is a
/// value a reader can say out loud.
function niceCeiling(value: number): number {
  if (value <= 0) {
    return 1
  }

  const step = 10 ** Math.floor(Math.log10(value)) / 2
  return Math.ceil(value / step) * step
}

/**
 * Received and resolved per UTC day, drawn by hand in SVG (ADR-046: the
 * platform suffices for two lines, so no chart library).
 *
 * The plot stretches to its box with `preserveAspectRatio="none"`, and
 * `vector-effect` keeps the strokes from stretching with it. The resolved
 * line is dashed as well as differently coloured, so the two can be told
 * apart without colour.
 *
 * There is no hover tooltip. The exact values are in a table behind "Show
 * the data", which a keyboard and a screen reader can both reach.
 */
export function VolumeChart({ days }: { days: DashboardDay[] }) {
  const received = days.map((day) => toNumber(day.received))
  const resolved = days.map((day) => toNumber(day.resolvedByReviewer))
  const max = niceCeiling(Math.max(...received, ...resolved) * 1.1)
  const last = days.length - 1

  const x = (index: number) => (last === 0 ? 0 : (index / last) * WIDTH)
  const y = (value: number) => HEIGHT - (value / max) * HEIGHT
  const points = (values: number[]) =>
    values.map((value, index) => `${x(index).toFixed(1)},${y(value).toFixed(1)}`).join(' ')

  const ticks = [1, 0.5, 0].map((fraction) => Math.round(max * fraction))

  const labelled = axisLabelDays(days.length).map((index) => ({ day: days[index], index }))

  const totalReceived = received.reduce((sum, value) => sum + value, 0)
  const totalResolved = resolved.reduce((sum, value) => sum + value, 0)
  const summary =
    `Tickets received and resolved by a reviewer per day, ` +
    `${formatDay(days[0].day)} to ${formatDay(days[last].day)}: ` +
    `${totalReceived} received, ${totalResolved} resolved.`

  return (
    <figure className="chart">
      <div className="chart__legend" aria-hidden="true">
        <span className="chart__key chart__key--received">Received</span>
        <span className="chart__key chart__key--resolved">Resolved by a reviewer</span>
      </div>

      <div className="chart__area">
        <div className="chart__y" aria-hidden="true">
          {ticks.map((tick) => (
            <span key={tick} style={{ top: `${(1 - tick / max) * 100}%` }}>
              {tick.toLocaleString()}
            </span>
          ))}
        </div>

        <svg
          className="chart__plot"
          viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
          preserveAspectRatio="none"
          role="img"
          aria-label={summary}
        >
          {ticks.map((tick) => (
            <line
              key={tick}
              className="chart__grid"
              x1={0}
              x2={WIDTH}
              y1={y(tick)}
              y2={y(tick)}
              vectorEffect="non-scaling-stroke"
            />
          ))}
          <polyline
            className="chart__line chart__line--received"
            points={points(received)}
            vectorEffect="non-scaling-stroke"
          />
          <polyline
            className="chart__line chart__line--resolved"
            points={points(resolved)}
            vectorEffect="non-scaling-stroke"
          />
        </svg>

        <div className="chart__x" aria-hidden="true">
          {labelled.map(({ day, index }) => (
            <span
              key={day.day}
              style={{
                left: `${last === 0 ? 0 : (index / last) * 100}%`,
                transform:
                  index === 0 ? 'none' : index === last ? 'translateX(-100%)' : 'translateX(-50%)',
              }}
            >
              {formatDay(day.day)}
            </span>
          ))}
        </div>
      </div>

      <figcaption className="muted">Days are UTC, and the last one is today so far.</figcaption>

      <details className="chart__data">
        <summary>Show the data</summary>
        <table>
          <caption className="visually-hidden">Tickets per day</caption>
          <thead>
            <tr>
              <th scope="col">Day</th>
              <th scope="col">Received</th>
              <th scope="col">Resolved by a reviewer</th>
            </tr>
          </thead>
          <tbody>
            {days.map((day) => (
              <tr key={day.day}>
                <th scope="row">{formatDay(day.day)}</th>
                <td>{toNumber(day.received)}</td>
                <td>{toNumber(day.resolvedByReviewer)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  )
}
