import { useEffect, useState } from 'react'
import {
  applyTheme,
  describePreference,
  nextPreference,
  readPreference,
  resolveTheme,
  writePreference,
  type ThemePreference,
} from './theme'

const DARK_QUERY = '(prefers-color-scheme: dark)'

/**
 * Cycles system → light → dark and persists the choice.
 *
 * The listener is the part worth keeping: while the preference is `system`,
 * the OS can change underneath the page - macOS and Windows both switch on a
 * schedule - and without it the app would keep showing yesterday's theme
 * until a reload. It stays subscribed regardless of preference because the
 * user can return to `system` at any point.
 */
export function ThemeToggle() {
  const [preference, setPreference] = useState<ThemePreference>(() =>
    readPreference(globalThis.localStorage),
  )
  const [systemPrefersDark, setSystemPrefersDark] = useState(
    () => globalThis.matchMedia?.(DARK_QUERY).matches ?? false,
  )

  useEffect(() => {
    applyTheme(document.documentElement, preference)
  }, [preference])

  useEffect(() => {
    const query = globalThis.matchMedia?.(DARK_QUERY)
    if (!query) {
      return
    }

    const onChange = (event: MediaQueryListEvent) => setSystemPrefersDark(event.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  const resolved = resolveTheme(preference, systemPrefersDark)

  const choose = () => {
    const next = nextPreference(preference)
    setPreference(next)
    writePreference(globalThis.localStorage, next)
  }

  return (
    <button
      type="button"
      className="theme-toggle"
      onClick={choose}
      // The visible text says which mode is selected; it does not say what
      // pressing does. Screen reader users get that from the label, which is
      // the one place it can be stated without cluttering the control.
      aria-label={`Theme: ${describePreference(preference)}. Activate to change.`}
    >
      <span aria-hidden="true">{resolved === 'dark' ? '◗' : '◖'}</span>
      {describePreference(preference)}
    </button>
  )
}
