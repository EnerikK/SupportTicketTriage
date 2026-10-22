/** What the user chose, which is not the same as what they see. */
export type ThemePreference = 'system' | 'light' | 'dark'

/** What is actually on screen once the system has had its say. */
export type ResolvedTheme = 'light' | 'dark'

const STORAGE_KEY = 'stt.theme'

/**
 * Three states, not two.
 *
 * A boolean "dark mode on/off" has to pick a default, and whichever it picks
 * is wrong for half the users: someone on a dark OS gets a light app until
 * they intervene, or the reverse. `system` is the honest third state - follow
 * the machine until told otherwise - and it is the default for that reason.
 */
export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark'
}

/**
 * Reads the stored preference, falling back to `system`.
 *
 * Storage can throw rather than merely return null - Safari in private mode
 * and browsers configured to block site data both do - so this never assumes
 * the read will succeed. A theme is a convenience; it must not be able to
 * stop the app rendering.
 */
export function readPreference(storage: Pick<Storage, 'getItem'> | undefined): ThemePreference {
  try {
    const stored = storage?.getItem(STORAGE_KEY)
    return isThemePreference(stored) ? stored : 'system'
  } catch {
    return 'system'
  }
}

export function writePreference(
  storage: Pick<Storage, 'setItem'> | undefined,
  preference: ThemePreference,
): void {
  try {
    storage?.setItem(STORAGE_KEY, preference)
  } catch {
    // A preference that cannot be saved is still worth applying for this
    // session; failing to persist is not a reason to fail to render.
  }
}

/** What the user sees, given what they chose and what the machine prefers. */
export function resolveTheme(preference: ThemePreference, systemPrefersDark: boolean): ResolvedTheme {
  if (preference === 'system') {
    return systemPrefersDark ? 'dark' : 'light'
  }

  return preference
}

/**
 * The order a toggle cycles through.
 *
 * System first, because it is the default and a user should be able to get
 * back to it. A two-way switch strands anyone who clicks once out of
 * curiosity: there would be no way back to "follow my machine" short of
 * clearing site data.
 */
export function nextPreference(current: ThemePreference): ThemePreference {
  switch (current) {
    case 'system':
      return 'light'
    case 'light':
      return 'dark'
    default:
      return 'system'
  }
}

/**
 * Stamps the choice onto the document.
 *
 * `data-theme` is absent for `system`, which is what lets the stylesheet's
 * `prefers-color-scheme` block apply. Writing `data-theme="system"` would
 * match neither of the explicit selectors and the page would fall back to
 * light on a dark machine.
 */
export function applyTheme(root: HTMLElement, preference: ThemePreference): void {
  if (preference === 'system') {
    root.removeAttribute('data-theme')
    return
  }

  root.setAttribute('data-theme', preference)
}

/** The label a toggle shows for a preference. Not an icon - see ThemeToggle. */
export function describePreference(preference: ThemePreference): string {
  switch (preference) {
    case 'light':
      return 'Light'
    case 'dark':
      return 'Dark'
    default:
      return 'System'
  }
}
