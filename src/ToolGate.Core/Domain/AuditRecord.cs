namespace ToolGate.Core.Domain;

public enum CallChannel
{
    Rest,
    Mcp,
}

public enum AuditAction
{
    ToolCall,
    ApprovalGranted,
    ApprovalRejected,
}

public enum CallOutcome
{
    Executed,
    Failed,
    Refused,
    PendingApproval,
    Rejected,
}

/// <summary>
/// One entry in the call audit trail. Every action traces back to a caller, a policy decision and, when
/// applicable, an approver.
/// </summary>
public class AuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public AuditAction Action { get; set; }

    public required string CallerSubject { get; set; }

    public string? CallerName { get; set; }

    public string? ClientId { get; set; }

    public CallChannel Channel { get; set; }

    public required string ToolName { get; set; }

    public string ArgumentsJson { get; set; } = "{}";

    public PolicyEffect Decision { get; set; }

    public string DecisionReason { get; set; } = string.Empty;

    public Guid? PolicyId { get; set; }

    public Guid? ApprovalRequestId { get; set; }

    public string? ApproverSubject { get; set; }

    public CallOutcome Outcome { get; set; }

    public int? UpstreamStatusCode { get; set; }

    public string? CorrelationId { get; set; }
}
