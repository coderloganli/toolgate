using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolGate.Core.Domain;
using ToolGate.Core.Invocation;
using ToolGate.Core.Persistence;
using ToolGate.Core.Policies;

namespace ToolGate.Core.Approvals;

public enum ApprovalResultKind
{
    Completed,
    NotFound,
    AlreadyDecided,
    SelfApprovalForbidden,
    NoLongerPermitted,
}

public sealed record ApprovalResult(ApprovalResultKind Kind, ApprovalRequest? Request = null, string? Message = null);

/// <summary>
/// The human-approval queue. Approving a held call re-checks the policy at execution time, so revoking a
/// policy also stops calls that are still waiting for approval.
/// </summary>
public sealed class ApprovalService(
    ToolGateDbContext db,
    IPolicyEngine policyEngine,
    IToolInvoker invoker,
    TimeProvider clock)
{
    private const int MaxStoredBodyLength = 16 * 1024;

    public Task<List<ApprovalRequest>> ListAsync(ApprovalStatus? status, CancellationToken ct)
    {
        var query = db.ApprovalRequests.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        return query.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<ApprovalResult> ApproveAsync(Guid id, CallerIdentity approver, string? comment, CancellationToken ct)
    {
        var request = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        var precondition = CheckDecidable(request, approver);
        if (precondition is not null)
        {
            return precondition;
        }

        var tool = await db.Tools.AsNoTracking().FirstOrDefaultAsync(t => t.Name == request!.ToolName, ct);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request!.ArgumentsJson)
            ?? new Dictionary<string, JsonElement>();
        var caller = new CallerIdentity(request.CallerSubject, request.CallerName, request.ClientId);
        var policies = await db.Policies.AsNoTracking().Include(p => p.Grants)
            .Where(p => p.IsEnabled && p.CallerSubject == request.CallerSubject)
            .ToListAsync(ct);
        var decision = policyEngine.Evaluate(caller, tool, arguments, policies);

        if (decision.Effect == PolicyEffect.Deny)
        {
            MarkDecided(request, ApprovalStatus.Rejected, approver, comment ?? $"Policy no longer permits this call: {decision.Reason}");
            AddAudit(request, AuditAction.ApprovalRejected, decision, approver, CallOutcome.Refused, null);
            return await SaveDecisionAsync(request, ApprovalResultKind.NoLongerPermitted, decision.Reason, ct);
        }

        MarkDecided(request, ApprovalStatus.Approved, approver, comment);
        var saved = await SaveDecisionAsync(request, ApprovalResultKind.Completed, null, ct);
        if (saved.Kind != ApprovalResultKind.Completed)
        {
            return saved;
        }

        var result = await invoker.InvokeAsync(tool!, arguments, ct);
        request.ResultStatusCode = result.StatusCode;
        request.ResultBody = result.Body.Length > MaxStoredBodyLength ? result.Body[..MaxStoredBodyLength] : result.Body;
        request.ConcurrencyStamp = Guid.NewGuid();
        AddAudit(request, AuditAction.ApprovalGranted, decision, approver,
            result.IsSuccess ? CallOutcome.Executed : CallOutcome.Failed, result.StatusCode);
        await db.SaveChangesAsync(ct);
        return new ApprovalResult(ApprovalResultKind.Completed, request);
    }

    public async Task<ApprovalResult> RejectAsync(Guid id, CallerIdentity approver, string? comment, CancellationToken ct)
    {
        var request = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id, ct);
        var precondition = CheckDecidable(request, approver);
        if (precondition is not null)
        {
            return precondition;
        }

        MarkDecided(request!, ApprovalStatus.Rejected, approver, comment);
        var decision = new PolicyDecision(PolicyEffect.RequireApproval, request!.DecisionReason, request.PolicyId);
        AddAudit(request, AuditAction.ApprovalRejected, decision, approver, CallOutcome.Rejected, null);
        return await SaveDecisionAsync(request, ApprovalResultKind.Completed, null, ct);
    }

    private static ApprovalResult? CheckDecidable(ApprovalRequest? request, CallerIdentity approver)
    {
        if (request is null)
        {
            return new ApprovalResult(ApprovalResultKind.NotFound);
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            return new ApprovalResult(ApprovalResultKind.AlreadyDecided, request, $"Request is already {request.Status}.");
        }

        if (string.Equals(request.CallerSubject, approver.Subject, StringComparison.Ordinal))
        {
            return new ApprovalResult(ApprovalResultKind.SelfApprovalForbidden, request,
                "A caller cannot decide its own approval request.");
        }

        return null;
    }

    private void MarkDecided(ApprovalRequest request, ApprovalStatus status, CallerIdentity approver, string? comment)
    {
        request.Status = status;
        request.DecidedBy = approver.Subject;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionComment = comment;
        request.ConcurrencyStamp = Guid.NewGuid();
    }

    private void AddAudit(
        ApprovalRequest request,
        AuditAction action,
        PolicyDecision decision,
        CallerIdentity approver,
        CallOutcome outcome,
        int? upstreamStatusCode)
    {
        db.AuditRecords.Add(new AuditRecord
        {
            Timestamp = clock.GetUtcNow(),
            Action = action,
            CallerSubject = request.CallerSubject,
            CallerName = request.CallerName,
            ClientId = request.ClientId,
            Channel = request.Channel,
            ToolName = request.ToolName,
            ArgumentsJson = request.ArgumentsJson,
            Decision = decision.Effect,
            DecisionReason = decision.Reason,
            PolicyId = decision.PolicyId,
            ApprovalRequestId = request.Id,
            ApproverSubject = approver.Subject,
            Outcome = outcome,
            UpstreamStatusCode = upstreamStatusCode,
        });
    }

    private async Task<ApprovalResult> SaveDecisionAsync(
        ApprovalRequest request, ApprovalResultKind kind, string? message, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new ApprovalResult(kind, request, message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ApprovalResult(ApprovalResultKind.AlreadyDecided, request, "Request was decided concurrently.");
        }
    }
}
