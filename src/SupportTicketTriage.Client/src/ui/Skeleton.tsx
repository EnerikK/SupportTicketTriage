import type { ReactNode } from 'react'

/**
 * A loading placeholder in the shape of the content that is coming.
 *
 * A skeleton rather than a spinner because its value is layout preservation:
 * the queue does not jump when eight rows of data replace eight rows of grey.
 * A spinner reserves no space and so guarantees the jump it is covering for.
 *
 * The pulse is an `animation` rather than a `transition`, which matters:
 * the single `prefers-reduced-motion` block at the foot of `index.css`
 * already neutralises it, and the element's resting state is opacity 1, so
 * reduced motion leaves a correct static bar rather than a half-faded one.
 */
export function Skeleton({ width, height }: { width?: string; height?: string }) {
  return <span className="skeleton" style={{ width, height }} />
}

/**
 * Wraps a set of skeletons with the one thing a screen reader actually wants.
 *
 * The bars themselves are decoration and are hidden; announcing eleven grey
 * rectangles helps nobody. `role="status"` is polite by definition, so a
 * reader finishing a sentence is not interrupted by "loading" - which is the
 * difference between this and `role="alert"`, used for the failures.
 */
export function LoadingRegion({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div role="status">
      <span className="visually-hidden">{label}</span>
      <div className="loading__body" aria-hidden="true">
        {children}
      </div>
    </div>
  )
}
