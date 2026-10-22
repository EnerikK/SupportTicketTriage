import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { StateIcon } from './StateIcon'
import type { TicketStateTone } from './ticketState'

const tones: TicketStateTone[] = ['reviewed', 'rejected', 'ready', 'blocked', 'pending']

describe('StateIcon', () => {
  /// The icon is a second encoding of a word that is already on screen, so it
  /// must contribute nothing to the accessibility tree. ADR-025 makes this a
  /// rule rather than a preference: an icon that reaches the tree inside a
  /// button changes that button's accessible name, and four tests assert
  /// those names exactly because they are what proves nothing can be sent.
  it.each(tones)('renders %s as a glyph hidden from assistive technology', (tone) => {
    const { container } = render(<StateIcon tone={tone} />)

    const svg = container.querySelector('svg')
    expect(svg).not.toBeNull()
    expect(svg).toHaveAttribute('aria-hidden', 'true')
    expect(container.textContent).toBe('')
  })

  /// Two tones sharing a glyph is silent: the column still renders, the words
  /// are still right, and the scanning cue the icons exist for is quietly
  /// gone for one of them.
  it('gives every tone a distinct glyph', () => {
    const glyphs = tones.map((tone) => {
      const { container, unmount } = render(<StateIcon tone={tone} />)
      const markup = container.innerHTML
      unmount()

      return markup
    })

    expect(new Set(glyphs).size).toBe(tones.length)
  })
})
