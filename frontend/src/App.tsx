import { BrowserRouter, Link, Navigate, Route, Routes } from 'react-router'
import { AddTransactionPage } from './pages/AddTransactionPage'
import { MonitorPage } from './pages/MonitorPage'

function App() {
  return (
    <BrowserRouter>
      <nav>
        <Link to="/add">Add transaction</Link>
        {' | '}
        <Link to="/monitor">Live monitor</Link>
      </nav>

      <Routes>
        <Route path="/" element={<Navigate to="/monitor" replace />} />
        <Route path="/add" element={<AddTransactionPage />} />
        <Route path="/monitor" element={<MonitorPage />} />
        <Route path="*" element={<h1>Page not found</h1>} />
      </Routes>
    </BrowserRouter>
  )
}

export default App