using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolGate.Core.Domain;
using ToolGate.Core.Invocation;
using ToolGate.Core.Persistence;
using ToolGate.Core.Policies;

namespace ToolGate.Core.Gateway;

public sealed record ToolCallRequest(
    CallerIdentity Caller,
    string ToolName,
    IReadOnlyDictionary<string, JsonElement> Arguments,
    CallChannel Channel,
    string? CorrelationId = null);

public enum ToolCallStatus
{
    Executed,
    Failed,
    Refused,
    PendingApproval,
    UnknownTool,
}

public sealed record ToolCallResult(
    ToolCallStatus Status,
    string Message,
    Guid AuditId,
    Guid? ApprovalRequestId = null,
    int? UpstreamStatusCode = null,
    string? Body = null);

/// <summary>
/// The single entry point for agent tool calls, shared by the REST API and the MCP server. Every call is
/// evaluated by the policy engine and written to the audit trail before anything reaches an internal service.
/// </summary>
public sealed class GatewayService(
    ToolGateDbContext db,
    IPolicyEngine policyEngine,
    IToolInvoker invoker,
    TimeProvider clock)
{
    /// <summary>Tools the caller is currently granted, for discovery via REST or MCP <c>tools/list</c>.</summary>
    public async Task<IReadOnlyList<ToolDefinition>> ListGrantedToolsAsync(CallerIdentity caller, CancellationToken ct)
    {
        var policies = await LoadPoliciesAsync(caller, ct);
        var tools = await db.Tools.AsNoTracking().Where(t => t.IsEnabled).OrderBy(t => t.Name).ToListAsync(ct);
        return tools.Where(t => policyEngine.IsGranted(caller, t, policies)).ToList();
    }

    public async Task<ToolCallResult> CallAsync(ToolCallRequest request, CancellationToken ct)
    {
        var tool = await db.Tools.AsNoTracking().FirstOrDefaultAsync(t => t.Name == request.ToolName, ct);
        var policies = await LoadPoliciesAsync(request.Caller, ct);
        var decision = policyEngine.Evaluate(request.Caller, tool, request.Arguments, policies);
        var argumentsJson = JsonSerializer.Serialize(request.Arguments);

        var audit = new AuditRecord
        {
            Timestamp = clock.GetUtcNow(),
            Action = AuditAction.ToolCall,
            CallerSubject = request.Caller.Subject,
            CallerName = request.Caller.DisplayName,
            ClientId = request.Caller.ClientId,
            Channel = request.Channel,
            ToolName = request.ToolName,
            ArgumentsJson = argumentsJson,
            Decision = decision.Effect,
            DecisionReason = decision.Reason,
            PolicyId = decision.PolicyId,
            CorrelationId = request.CorrelationId,
        };
        db.AuditRecords.Add(audit);

        switch (decision.Effect)
        {
            case PolicyEffect.Deny:
                audit.Outcome = CallOutcome.Refused;
                await db.SaveChangesAsync(ct);
                return new ToolCallResult(
                    tool is null ? ToolCallStatus.UnknownTool : ToolCallStatus.Refused, decision.Reason, audit.Id);

            case PolicyEffect.RequireApproval:
                var approval = new ApprovalRequest
                {
                    CreatedAt = audit.Timestamp,
                    CallerSubject = request.Caller.Subject,
                    CallerName = request.Caller.DisplayName,
                    ClientId = request.Caller.ClientId,
                    Channel = request.Channel,
                    ToolName = request.ToolName,
                    ArgumentsJson = argumentsJson,
                    PolicyId = decision.PolicyId,
                    DecisionReason = decision.Reason,
                };
                db.ApprovalRequests.Add(approval);
                audit.Outcome = CallOutcome.PendingApproval;
                audit.ApprovalRequestId = approval.Id;
                await db.SaveChangesAsync(ct);
                return new ToolCallResult(ToolCallStatus.PendingApproval, decision.Reason, audit.Id, approval.Id);

            default:
                var result = await invoker.InvokeAsync(tool!, request.Arguments, ct);
                audit.Outcome = result.IsSuccess ? CallOutcome.Executed : CallOutcome.Failed;
                audit.UpstreamStatusCode = result.StatusCode;
                await db.SaveChangesAsync(ct);
                return new ToolCallResult(
                    result.IsSuccess ? ToolCallStatus.Executed : ToolCallStatus.Failed,
                    decision.Reason,
                    audit.Id,
                    UpstreamStatusCode: result.StatusCode,
                    Body: result.Body);
        }
    }

    private Task<List<Policy>> LoadPoliciesAsync(CallerIdentity caller, CancellationToken ct) =>
        db.Policies.AsNoTracking()
            .Include(p => p.Grants)
            .Where(p => p.IsEnabled && p.CallerSubject == caller.Subject)
            .ToListAsync(ct);
}
