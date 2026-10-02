using Microsoft.EntityFrameworkCore;
using ToolGate.Core.Domain;
using ToolGate.Core.Gateway;

namespace ToolGate.Tests;

public class GatewayServiceTests
{
    private static readonly CallerIdentity Agent = new("agent-1", "Support agent", "client-app");

    [Fact]
    public async Task Allowed_call_is_executed_and_audited()
    {
        using var f = new GatewayFixture();
        var policy = await f.AddPolicyAsync("agent-1", "crm.lookup", "customerId");
        await f.AddToolAsync("crm.lookup");

        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "crm.lookup", GatewayFixture.Args(new { customerId = "c-1" }), CallChannel.Rest, "trace-1"),
            CancellationToken.None);

        Assert.Equal(ToolCallStatus.Executed, result.Status);
        Assert.Single(f.Invoker.Calls);
        var audit = await f.Db.AuditRecords.SingleAsync();
        Assert.Equal(CallOutcome.Executed, audit.Outcome);
        Assert.Equal(PolicyEffect.Allow, audit.Decision);
        Assert.Equal(policy.Id, audit.PolicyId);
        Assert.Equal("agent-1", audit.CallerSubject);
        Assert.Equal("client-app", audit.ClientId);
        Assert.Equal(200, audit.UpstreamStatusCode);
        Assert.Equal("trace-1", audit.CorrelationId);
    }

    [Fact]
    public async Task Upstream_failure_is_reported_and_audited()
    {
        using var f = new GatewayFixture();
        await f.AddPolicyAsync("agent-1", "crm.lookup");
        await f.AddToolAsync("crm.lookup");
        f.Invoker.NextResult = new(false, 500, "boom");

        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "crm.lookup", GatewayFixture.Args(new { }), CallChannel.Mcp), CancellationToken.None);

        Assert.Equal(ToolCallStatus.Failed, result.Status);
        Assert.Equal(CallOutcome.Failed, (await f.Db.AuditRecords.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Out_of_scope_call_is_refused_without_reaching_the_service()
    {
        using var f = new GatewayFixture();
        await f.AddPolicyAsync("agent-1", "crm.lookup", "customerId");
        await f.AddToolAsync("crm.lookup");

        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "crm.lookup", GatewayFixture.Args(new { customerId = "c-1", dump = true }), CallChannel.Rest),
            CancellationToken.None);

        Assert.Equal(ToolCallStatus.Refused, result.Status);
        Assert.Empty(f.Invoker.Calls);
        var audit = await f.Db.AuditRecords.SingleAsync();
        Assert.Equal(CallOutcome.Refused, audit.Outcome);
        Assert.Equal(PolicyEffect.Deny, audit.Decision);
    }

    [Fact]
    public async Task Unknown_tool_is_reported_and_audited()
    {
        using var f = new GatewayFixture();

        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "nope", GatewayFixture.Args(new { }), CallChannel.Mcp), CancellationToken.None);

        Assert.Equal(ToolCallStatus.UnknownTool, result.Status);
        Assert.Equal(CallOutcome.Refused, (await f.Db.AuditRecords.SingleAsync()).Outcome);
    }

    [Fact]
    public async Task Sensitive_call_is_held_for_approval()
    {
        using var f = new GatewayFixture();
        await f.AddPolicyAsync("agent-1", "billing.refund", "orderId");
        await f.AddToolAsync("billing.refund", sensitive: true);

        var result = await f.Gateway.CallAsync(
            new ToolCallRequest(Agent, "billing.refund", GatewayFixture.Args(new { orderId = "o-9" }), CallChannel.Mcp),
            CancellationToken.None);

        Assert.Equal(ToolCallStatus.PendingApproval, result.Status);
        Assert.Empty(f.Invoker.Calls);
        var approval = await f.Db.ApprovalRequests.SingleAsync();
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(result.ApprovalRequestId, approval.Id);
        Assert.Equal(approval.Id, (await f.Db.AuditRecords.SingleAsync()).ApprovalRequestId);
    }

    [Fact]
    public async Task Lists_only_granted_tools()
    {
        using var f = new GatewayFixture();
        await f.AddToolAsync("crm.lookup");
        await f.AddToolAsync("billing.refund");
        await f.AddPolicyAsync("agent-1", "crm.lookup");

        var tools = await f.Gateway.ListGrantedToolsAsync(Agent, CancellationToken.None);

        Assert.Equal(new[] { "crm.lookup" }, tools.Select(t => t.Name));
    }
}
