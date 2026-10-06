import { Link, Route, Routes } from 'react-router'
import { TicketQueue } from './features/queue/TicketQueue'
import { TicketDetail } from './features/ticket/TicketDetail'

export function App() {
  return (
    <div className="app">
      <header className="app__header">
        <Link to="/" className="app__title">
          Support Ticket Triage
        </Link>
        <p className="app__tagline">
          Drafts are grounded in resolved tickets and reviewed by a human. Nothing is ever sent.
        </p>
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
