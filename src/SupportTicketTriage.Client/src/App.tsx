import { Link, Navigate, NavLink, Route, Routes } from 'react-router'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { TicketQueue } from './features/queue/TicketQueue'
import { TicketDetail } from './features/ticket/TicketDetail'
import { ThemeToggle } from './theme/ThemeToggle'

/// A mistyped or outdated address. Without this the header sat over an empty
/// page, which reads as a broken app rather than a wrong address.
function NotFound() {
  return (
    <div className="empty">
      <p className="empty__title">There is no page at this address.</p>
      <p className="empty__detail">
        Go to the <Link to="/dashboard">dashboard</Link> or the <Link to="/queue">queue</Link>.
      </p>
    </div>
  )
}

export function App() {
  return (
    <div className="app">
      <header className="app__header">
        <div>
          {/* The page had no `h1` at all and its outline started at `h2`.
              The app name takes it, which makes the outline on the detail
              page read h1 app, h2 subject, h3 section, h4 citations.

              The trade-off, stated: an `h1` naming the application rather
              than the page is the weaker of the two correct options. The
              stronger one gives each route its own `h1`, but the queue has
              no heading to promote and inventing a visible one is a design
              change rather than an accessibility fix. */}
          <h1 className="app__title">
            <Link to="/">Support Ticket Triage</Link>
          </h1>
          <p className="app__tagline">
            Drafts are grounded in resolved tickets and reviewed by a human. Nothing is ever sent.
          </p>
          {/* NavLink marks the current page with aria-current="page", which
              is what both the styling and a screen reader read. */}
          <nav aria-label="Primary" className="app__nav">
            <NavLink to="/dashboard">Dashboard</NavLink>
            <NavLink to="/queue">Queue</NavLink>
          </nav>
        </div>
        <ThemeToggle />
      </header>

      <main>
        {/* The app opens on the dashboard (ADR-046, 2026-11-22). `/` is a
            redirect rather than a second address for the dashboard, so every
            screen's address names it. `replace` swaps the history entry:
            without it, Back lands on `/` and is sent straight forward again. */}
        <Routes>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/queue" element={<TicketQueue />} />
          <Route path="/tickets/:id" element={<TicketDetail />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
    </div>
  )
}
