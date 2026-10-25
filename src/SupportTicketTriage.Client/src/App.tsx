import { Link, Route, Routes } from 'react-router'
import { TicketQueue } from './features/queue/TicketQueue'
import { TicketDetail } from './features/ticket/TicketDetail'
import { ThemeToggle } from './theme/ThemeToggle'

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
        </div>
        <ThemeToggle />
      </header>

      <main>
        <Routes>
          <Route path="/" element={<TicketQueue />} />
          <Route path="/tickets/:id" element={<TicketDetail />} />
        </Routes>
      </main>
    </div>
  )
}
