export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected'
export type CallOutcome = 'Executed' | 'Failed' | 'Refused' | 'PendingApproval' | 'Rejected'
export type CallChannel = 'Rest' | 'Mcp'

export interface ApprovalRequest {
  id: string
  createdAt: string
  callerSubject: string
  callerName: string | null
  clientId: string | null
  channel: CallChannel
  toolName: string
  argumentsJson: string
  policyId: string | null
  decisionReason: string
  status: ApprovalStatus
  decidedBy: string | null
  decidedAt: string | null
  decisionComment: string | null
  resultStatusCode: number | null
  resultBody: string | null
}

export interface AuditRecord {
  id: string
  timestamp: string
  action: 'ToolCall' | 'ApprovalGranted' | 'ApprovalRejected'
  callerSubject: string
  callerName: string | null
  clientId: string | null
  channel: CallChannel
  toolName: string
  argumentsJson: string
  decision: 'Deny' | 'Allow' | 'RequireApproval'
  decisionReason: string
  policyId: string | null
  approvalRequestId: string | null
  approverSubject: string | null
  outcome: CallOutcome
  upstreamStatusCode: number | null
  correlationId: string | null
}

export interface AuditPage {
  items: AuditRecord[]
  total: number
  page: number
  pageSize: number
}

export interface AuditFilter {
  caller?: string
  tool?: string
  outcome?: CallOutcome | ''
  approver?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

const TOKEN_KEY = 'toolgate.token'

export function getToken(): string {
  try {
    return localStorage.getItem(TOKEN_KEY) ?? ''
  } catch {
    return ''
  }
}

export function setToken(token: string): void {
  try {
    localStorage.setItem(TOKEN_KEY, token)
  } catch {
    // Storage can be unavailable (private mode); the token then lasts only for this page.
  }
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${getToken()}`,
      ...init?.headers,
    },
  })

  if (!response.ok) {
    let message = response.statusText
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
    } catch {
      // Non-JSON error body; keep the status text.
    }
    throw new ApiError(response.status, message)
  }

  return (await response.json()) as T
}

export function listApprovals(status?: ApprovalStatus): Promise<ApprovalRequest[]> {
  const query = status ? `?status=${status}` : ''
  return request<ApprovalRequest[]>(`/api/approvals${query}`)
}

export function decideApproval(id: string, decision: 'approve' | 'reject', comment: string): Promise<ApprovalRequest> {
  return request<ApprovalRequest>(`/api/approvals/${id}/${decision}`, {
    method: 'POST',
    body: JSON.stringify({ comment: comment || null }),
  })
}

export function queryAudit(filter: AuditFilter): Promise<AuditPage> {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== '') {
      params.set(key, String(value))
    }
  }
  return request<AuditPage>(`/api/audit?${params}`)
}
