import { useCallback, useEffect, useState } from 'react'
import { decideApproval, listApprovals, type ApprovalRequest, type ApprovalStatus } from '../api.ts'

const statuses: ApprovalStatus[] = ['Pending', 'Approved', 'Rejected']

export default function ApprovalsPage({ token }: { token: string }) {
  const [status, setStatus] = useState<ApprovalStatus>('Pending')
  const [requests, setRequests] = useState<ApprovalRequest[]>([])
  const [comments, setComments] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!token) {
      setRequests([])
      return
    }
    try {
      setError(null)
      setRequests(await listApprovals(status))
    } catch (e) {
      setError((e as Error).message)
    }
  }, [status, token])

  useEffect(() => {
    void load()
  }, [load])

  async function decide(id: string, decision: 'approve' | 'reject') {
    setBusyId(id)
    try {
      setError(null)
      await decideApproval(id, decision, comments[id] ?? '')
      await load()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setBusyId(null)
    }
  }

  return (
    <section>
      <div className="toolbar">
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value as ApprovalStatus)}>
            {statuses.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </select>
        </label>
        <button onClick={() => void load()}>Refresh</button>
      </div>

      {!token && <p className="notice">Paste a bearer token with the operator role to load approvals.</p>}
      {error && <p className="error">{error}</p>}
      {token && requests.length === 0 && !error && <p className="notice">No {status.toLowerCase()} requests.</p>}

      {requests.map((request) => (
        <article key={request.id} className="card">
          <div className="card-head">
            <strong>{request.toolName}</strong>
            <span className={`badge ${request.status.toLowerCase()}`}>{request.status}</span>
          </div>
          <dl>
            <dt>Caller</dt>
            <dd>
              {request.callerName ? `${request.callerName} (${request.callerSubject})` : request.callerSubject}
              {request.clientId && ` via ${request.clientId}`} over {request.channel}
            </dd>
            <dt>Requested</dt>
            <dd>{new Date(request.createdAt).toLocaleString()}</dd>
            <dt>Reason held</dt>
            <dd>{request.decisionReason}</dd>
            <dt>Arguments</dt>
            <dd>
              <pre>{JSON.stringify(JSON.parse(request.argumentsJson), null, 2)}</pre>
            </dd>
            {request.decidedBy && (
              <>
                <dt>Decided</dt>
                <dd>
                  {request.status} by {request.decidedBy}
                  {request.decidedAt && ` at ${new Date(request.decidedAt).toLocaleString()}`}
                  {request.decisionComment && `: ${request.decisionComment}`}
                </dd>
              </>
            )}
            {request.resultStatusCode !== null && (
              <>
                <dt>Upstream status</dt>
                <dd>{request.resultStatusCode}</dd>
              </>
            )}
          </dl>
          {request.status === 'Pending' && (
            <div className="actions">
              <input
                placeholder="Comment (optional)"
                value={comments[request.id] ?? ''}
                onChange={(event) => setComments({ ...comments, [request.id]: event.target.value })}
              />
              <button className="approve" disabled={busyId === request.id} onClick={() => void decide(request.id, 'approve')}>
                Approve and execute
              </button>
              <button className="reject" disabled={busyId === request.id} onClick={() => void decide(request.id, 'reject')}>
                Reject
              </button>
            </div>
          )}
        </article>
      ))}
    </section>
  )
}
