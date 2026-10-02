using Microsoft.EntityFrameworkCore;
using ToolGate.Core.Approvals;
using ToolGate.Core.Domain;
using ToolGate.Core.Gateway;

namespace ToolGate.Tests;

public class ApprovalServiceTests
{
    private static readonly CallerIdentity Agent = new("agent-1");
    private static readonly CallerIdentity Operator = new("operator-1", "Ops");

    private static async Task<(GatewayFixture Fixture, Guid ApprovalId)> HeldCallAsync()
    {
        var f = new GatewayFixture();
        await f.AddPolicyAsync("agent-1", "billing.refund", "orderId");
        await f.AddToolAsync("billing.refund", sensitive: true);
        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "billing.refund", GatewayFixture.Args(new { orderId = "o-9" }), CallChannel.Rest),
            CancellationToken.None);
        f.Db.ChangeTracker.Clear();
        return (f, result.ApprovalRequestId!.Value);
    }

    [Fact]
    public async Task Approval_executes_the_call_and_records_the_approver()
    {
        var (f, id) = await HeldCallAsync();
        using var fixture = f;

        var result = await f.Approvals.ApproveAsync(id, Operator, "verified with customer", CancellationToken.None);

        Assert.Equal(ApprovalResultKind.Completed, result.Kind);
        Assert.Equal(ApprovalStatus.Approved, result.Request!.Status);
        Assert.Equal("operator-1", result.Request.DecidedBy);
        Assert.Single(f.Invoker.Calls);
        var audit = await f.Db.AuditRecords.SingleAsync(a => a.Action == AuditAction.ApprovalGranted);
        Assert.Equal("operator-1", audit.ApproverSubject);
        Assert.Equal("agent-1", audit.CallerSubject);
        Assert.Equal(CallOutcome.Executed, audit.Outcome);
        Assert.Equal(id, audit.ApprovalRequestId);
    }

    [Fact]
    public async Task Rejection_does_not_execute_the_call()
    {
        var (f, id) = await HeldCallAsync();
        using var fixture = f;

        var result = await f.Approvals.RejectAsync(id, Operator, "not authorized by customer", CancellationToken.None);

        Assert.Equal(ApprovalResultKind.Completed, result.Kind);
        Assert.Equal(ApprovalStatus.Rejected, result.Request!.Status);
        Assert.Empty(f.Invoker.Calls);
        var audit = await f.Db.AuditRecords.SingleAsync(a => a.Action == AuditAction.ApprovalRejected);
        Assert.Equal(CallOutcome.Rejected, audit.Outcome);
        Assert.Equal("operator-1", audit.ApproverSubject);
    }

    [Fact]
    public async Task Caller_cannot_approve_its_own_request()
    {
        var (f, id) = await HeldCallAsync();
        using var fixture = f;

        var result = await f.Approvals.ApproveAsync(id, Agent, null, CancellationToken.None);

        Assert.Equal(ApprovalResultKind.SelfApprovalForbidden, result.Kind);
        Assert.Empty(f.Invoker.Calls);
    }

    [Fact]
    public async Task Decided_request_cannot_be_decided_again()
    {
        var (f, id) = await HeldCallAsync();
        using var fixture = f;
        await f.Approvals.RejectAsync(id, Operator, null, CancellationToken.None);

        var result = await f.Approvals.ApproveAsync(id, Operator, null, CancellationToken.None);

        Assert.Equal(ApprovalResultKind.AlreadyDecided, result.Kind);
        Assert.Empty(f.Invoker.Calls);
    }

    [Fact]
    public async Task Approval_is_refused_when_the_policy_was_revoked()
    {
        var (f, id) = await HeldCallAsync();
        using var fixture = f;
        var policy = await f.Db.Policies.SingleAsync();
        policy.IsEnabled = false;
        await f.Db.SaveChangesAsync();

        var result = await f.Approvals.ApproveAsync(id, Operator, null, CancellationToken.None);

        Assert.Equal(ApprovalResultKind.NoLongerPermitted, result.Kind);
        Assert.Equal(ApprovalStatus.Rejected, result.Request!.Status);
        Assert.Empty(f.Invoker.Calls);
    }

    [Fact]
    public async Task Unknown_request_is_not_found()
    {
        using var f = new GatewayFixture();

        var result = await f.Approvals.ApproveAsync(Guid.NewGuid(), Operator, null, CancellationToken.None);

        Assert.Equal(ApprovalResultKind.NotFound, result.Kind);
    }
}
