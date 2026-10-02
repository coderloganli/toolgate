import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { queryAudit, type AuditFilter, type AuditPage as AuditResult, type CallOutcome } from '../api.ts'

const outcomes: CallOutcome[] = ['Executed', 'Failed', 'Refused', 'PendingApproval', 'Rejected']
const pageSize = 50

export default function AuditPage({ token }: { token: string }) {
  const [draft, setDraft] = useState<AuditFilter>({})
  const [filter, setFilter] = useState<AuditFilter>({ page: 1 })
  const [result, setResult] = useState<AuditResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!token) {
      setResult(null)
      return
    }
    try {
      setError(null)
      setResult(await queryAudit({ ...filter, pageSize }))
    } catch (e) {
      setError((e as Error).message)
    }
  }, [filter, token])

  useEffect(() => {
    void load()
  }, [load])

  function submit(event: FormEvent) {
    event.preventDefault()
    setFilter({
      ...draft,
      from: draft.from ? new Date(draft.from).toISOString() : undefined,
      to: draft.to ? new Date(draft.to).toISOString() : undefined,
      page: 1,
    })
  }

  const page = filter.page ?? 1
  const lastPage = result ? Math.max(1, Math.ceil(result.total / pageSize)) : 1

  return (
    <section>
      <form className="toolbar" onSubmit={submit}>
        <label>
          Caller
          <input value={draft.caller ?? ''} onChange={(e) => setDraft({ ...draft, caller: e.target.value })} />
        </label>
        <label>
          Tool
          <input value={draft.tool ?? ''} onChange={(e) => setDraft({ ...draft, tool: e.target.value })} />
        </label>
        <label>
          Approver
          <input value={draft.approver ?? ''} onChange={(e) => setDraft({ ...draft, approver: e.target.value })} />
        </label>
        <label>
          Outcome
          <select value={draft.outcome ?? ''} onChange={(e) => setDraft({ ...draft, outcome: e.target.value as CallOutcome | '' })}>
            <option value="">Any</option>
            {outcomes.map((o) => (
              <option key={o}>{o}</option>
            ))}
          </select>
        </label>
        <label>
          From
          <input type="datetime-local" value={draft.from ?? ''} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />
        </label>
        <label>
          To
          <input type="datetime-local" value={draft.to ?? ''} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />
        </label>
        <button type="submit">Search</button>
      </form>

      {!token && <p className="notice">Paste a bearer token with the operator role to query the audit trail.</p>}
      {error && <p className="error">{error}</p>}

      {result && (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Time</th>
                  <th>Action</th>
                  <th>Caller</th>
                  <th>Tool</th>
                  <th>Decision</th>
                  <th>Outcome</th>
                  <th>Approver</th>
                  <th>Upstream</th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((record) => (
                  <tr key={record.id} title={`${record.decisionReason}\n${record.argumentsJson}`}>
                    <td>{new Date(record.timestamp).toLocaleString()}</td>
                    <td>{record.action}</td>
                    <td>
                      {record.callerSubject}
                      <span className="muted"> ({record.channel})</span>
                    </td>
                    <td>{record.toolName}</td>
                    <td>{record.decision}</td>
                    <td>
                      <span className={`badge ${record.outcome.toLowerCase()}`}>{record.outcome}</span>
                    </td>
                    <td>{record.approverSubject ?? ''}</td>
                    <td>{record.upstreamStatusCode ?? ''}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="pager">
            <button disabled={page <= 1} onClick={() => setFilter({ ...filter, page: page - 1 })}>
              Previous
            </button>
            <span>
              Page {page} of {lastPage} ({result.total} records)
            </span>
            <button disabled={page >= lastPage} onClick={() => setFilter({ ...filter, page: page + 1 })}>
              Next
            </button>
          </div>
        </>
      )}
    </section>
  )
}
