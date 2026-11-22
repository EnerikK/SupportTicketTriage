import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'

/// The routing is what is under test, so every request stays pending: each
/// screen shows its loading state, which is enough to tell which one rendered.
function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

function navLink(name: 'Dashboard' | 'Queue') {
  return within(screen.getByRole('navigation', { name: 'Primary' })).getByRole('link', { name })
}

describe('App', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise(() => {})),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  /// NavLink marks a link current only when the location matches it, so the
  /// dashboard link being current shows the address really moved to
  /// /dashboard rather than the dashboard rendering at "/".
  it('opens on the dashboard', () => {
    renderAt('/')

    expect(screen.getByRole('status')).toHaveTextContent('Loading the dashboard')
    expect(navLink('Dashboard')).toHaveAttribute('aria-current', 'page')
    expect(navLink('Queue')).not.toHaveAttribute('aria-current')
  })

  it('puts the queue at its own address', () => {
    renderAt('/queue')

    expect(screen.getByRole('status')).toHaveTextContent('Loading the queue')
    expect(navLink('Queue')).toHaveAttribute('href', '/queue')
    expect(navLink('Queue')).toHaveAttribute('aria-current', 'page')
  })

  it('says so when nothing lives at an address, with a way back', () => {
    renderAt('/no-such-page')

    const main = within(screen.getByRole('main'))

    expect(main.getByText('There is no page at this address.')).toBeInTheDocument()
    expect(main.getByRole('link', { name: 'dashboard' })).toHaveAttribute('href', '/dashboard')
    expect(main.getByRole('link', { name: 'queue' })).toHaveAttribute('href', '/queue')
    expect(navLink('Dashboard')).not.toHaveAttribute('aria-current')
    expect(navLink('Queue')).not.toHaveAttribute('aria-current')
  })
})
