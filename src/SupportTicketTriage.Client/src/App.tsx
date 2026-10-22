import { Link, Route, Routes } from 'react-router'
import { TicketQueue } from './features/queue/TicketQueue'
import { TicketDetail } from './features/ticket/TicketDetail'
import { ThemeToggle } from './theme/ThemeToggle'

export function App() {
  return (
    <div className="app">
      <header className="app__header">
        <div>
          <Link to="/" className="app__title">
            Support Ticket Triage
          </Link>
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
