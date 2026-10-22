import { describe, expect, it } from 'vitest'
import {
  applyTheme,
  describePreference,
  isThemePreference,
  nextPreference,
  readPreference,
  resolveTheme,
  writePreference,
} from './theme'

describe('theme preference', () => {
  it('defaults to following the system', () => {
    expect(readPreference(undefined)).toBe('system')
    expect(readPreference({ getItem: () => null })).toBe('system')
  })

  it('reads a stored preference back', () => {
    expect(readPreference({ getItem: () => 'dark' })).toBe('dark')
    expect(readPreference({ getItem: () => 'light' })).toBe('light')
  })

  /// Anything could be in storage - another app's key, a half-written value,
  /// something a user typed into devtools. An unrecognised value is not a
  /// reason to render an unthemed page.
  it('ignores a stored value that is not a preference', () => {
    expect(readPreference({ getItem: () => 'purple' })).toBe('system')
    expect(readPreference({ getItem: () => '' })).toBe('system')
  })

  /// Storage throws rather than returning null in a private window and under
  /// a block-site-data setting. A theme is a convenience and must never be
  /// able to stop the app rendering.
  it('survives storage that throws', () => {
    const hostile = {
      getItem: () => {
        throw new Error('SecurityError')
      },
    }

    expect(readPreference(hostile)).toBe('system')
  })

  it('does not throw when a preference cannot be saved', () => {
    const hostile = {
      setItem: () => {
        throw new Error('QuotaExceededError')
      },
    }

    expect(() => writePreference(hostile, 'dark')).not.toThrow()
  })

  it('recognises only the three preferences', () => {
    expect(isThemePreference('system')).toBe(true)
    expect(isThemePreference('light')).toBe(true)
    expect(isThemePreference('dark')).toBe(true)
    expect(isThemePreference('auto')).toBe(false)
    expect(isThemePreference(null)).toBe(false)
  })
})

describe('resolving what is shown', () => {
  it('follows the system when the preference is system', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  /// The case a two-state toggle gets wrong: an explicit choice has to beat
  /// the machine, in both directions.
  it('lets an explicit choice override the system', () => {
    expect(resolveTheme('light', true)).toBe('light')
    expect(resolveTheme('dark', false)).toBe('dark')
  })
})

describe('cycling', () => {
  /// System is reachable again, which a two-way switch would not allow - a
  /// user who clicked once out of curiosity would be stranded on an explicit
  /// choice with no way back short of clearing site data.
  it('returns to system after three presses', () => {
    expect(nextPreference('system')).toBe('light')
    expect(nextPreference('light')).toBe('dark')
    expect(nextPreference('dark')).toBe('system')
  })
})

describe('applying to the document', () => {
  /// The attribute must be absent for system, not set to "system". Present
  /// with that value it matches neither explicit selector, and the stylesheet
  /// falls back to light on a dark machine.
  it('removes the attribute for system so prefers-color-scheme applies', () => {
    const root = document.createElement('html')
    root.setAttribute('data-theme', 'dark')

    applyTheme(root, 'system')

    expect(root.hasAttribute('data-theme')).toBe(false)
  })

  it('stamps an explicit choice', () => {
    const root = document.createElement('html')

    applyTheme(root, 'dark')
    expect(root.getAttribute('data-theme')).toBe('dark')

    applyTheme(root, 'light')
    expect(root.getAttribute('data-theme')).toBe('light')
  })
})

describe('labels', () => {
  it('names each preference', () => {
    expect(describePreference('system')).toBe('System')
    expect(describePreference('light')).toBe('Light')
    expect(describePreference('dark')).toBe('Dark')
  })
})
