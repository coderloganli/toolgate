import { useState } from 'react'
import { getToken, setToken } from './api.ts'
import ApprovalsPage from './pages/ApprovalsPage.tsx'
import AuditPage from './pages/AuditPage.tsx'

type Page = 'approvals' | 'audit'

export default function App() {
  const [page, setPage] = useState<Page>('approvals')
  const [token, setTokenState] = useState(getToken())

  function updateToken(value: string) {
    setTokenState(value)
    setToken(value)
  }

  return (
    <div className="layout">
      <header className="header">
        <h1>ToolGate Console</h1>
        <nav>
          <button className={page === 'approvals' ? 'tab active' : 'tab'} onClick={() => setPage('approvals')}>
            Approvals
          </button>
          <button className={page === 'audit' ? 'tab active' : 'tab'} onClick={() => setPage('audit')}>
            Audit trail
          </button>
        </nav>
        <label className="token">
          Bearer token
          <input
            type="password"
            value={token}
            placeholder="Paste an operator access token"
            onChange={(event) => updateToken(event.target.value.trim())}
          />
        </label>
      </header>
      <main>{page === 'approvals' ? <ApprovalsPage token={token} /> : <AuditPage token={token} />}</main>
    </div>
  )
}
