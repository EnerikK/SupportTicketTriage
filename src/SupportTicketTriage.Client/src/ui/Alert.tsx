import { CircleAlert } from 'lucide-react'
import type { ReactNode } from 'react'

/**
 * Every failure path in the application renders through here.
 *
 * `role="alert"` is kept on all four: each appears in response to something
 * the reviewer just did or something that just broke, which is precisely what
 * an assertive live region is for.
 *
 * The icon is `aria-hidden` and carries nothing. It is a second encoding of
 * the border and the colour - before this, a failure was signalled by turning
 * text red and nothing else, which is colour as the sole carrier of meaning.
 *
 * What does not change: the message always names what failed. ADR-023's rule
 * that the interface never hides what the system does not know applies to its
 * own failures first, so "something went wrong" is not available here.
 */
export function Alert({ children }: { children: ReactNode }) {
  return (
    <div className="alert" role="alert">
      <CircleAlert className="alert__icon" size={18} aria-hidden="true" focusable="false" />
      <p className="alert__message">{children}</p>
    </div>
  )
}
