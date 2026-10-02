namespace ToolGate.Core.Domain;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// A tool call held for human approval instead of being executed.
/// </summary>
public class ApprovalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public required string CallerSubject { get; set; }

    public string? CallerName { get; set; }

    public string? ClientId { get; set; }

    public CallChannel Channel { get; set; }

    public required string ToolName { get; set; }

    public string ArgumentsJson { get; set; } = "{}";

    public Guid? PolicyId { get; set; }

    public string DecisionReason { get; set; } = string.Empty;

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public int? ResultStatusCode { get; set; }

    public string? ResultBody { get; set; }

    /// <summary>Optimistic concurrency token so two operators cannot decide the same request.</summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
