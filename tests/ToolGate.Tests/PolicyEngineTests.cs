using System.Text.Json;
using ToolGate.Core.Domain;
using ToolGate.Core.Policies;

namespace ToolGate.Tests;

public class PolicyEngineTests
{
    private static readonly CallerIdentity Agent = new("agent-1");
    private readonly PolicyEngine _engine = new();

    private static ToolDefinition Tool(string name = "crm.lookup", bool sensitive = false, bool enabled = true) =>
        new() { Name = name, EndpointUrl = "https://crm.internal/lookup", IsSensitive = sensitive, IsEnabled = enabled };

    private static Policy PolicyFor(string subject, params ToolGrant[] grants) =>
        new() { Name = $"policy-{subject}-{Guid.NewGuid():N}", CallerSubject = subject, Grants = [.. grants] };

    private static ToolGrant Grant(string tool, bool requireApproval = false, params ParameterRule[] rules) =>
        new() { ToolName = tool, Parameters = [.. rules], RequireApproval = requireApproval };

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    [Fact]
    public void Denies_unregistered_tool()
    {
        var decision = _engine.Evaluate(Agent, null, Args(new { }), []);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
    }

    [Fact]
    public void Denies_disabled_tool_even_when_granted()
    {
        var policies = new[] { PolicyFor("agent-1", Grant("crm.lookup")) };

        var decision = _engine.Evaluate(Agent, Tool(enabled: false), Args(new { }), policies);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
    }

    [Fact]
    public void Denies_when_no_policy_binds_the_caller()
    {
        var policies = new[] { PolicyFor("someone-else", Grant("crm.lookup")) };

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { }), policies);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
        Assert.Null(decision.PolicyId);
    }

    [Fact]
    public void Denies_when_policy_grants_a_different_tool()
    {
        var policies = new[] { PolicyFor("agent-1", Grant("billing.refund")) };

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { }), policies);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
    }

    [Fact]
    public void Ignores_disabled_policies()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup"));
        policy.IsEnabled = false;

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { }), [policy]);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
    }

    [Fact]
    public void Allows_call_with_allowlisted_parameters()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("customerId")));

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { customerId = "c-42" }), [policy]);

        Assert.Equal(PolicyEffect.Allow, decision.Effect);
        Assert.Equal(policy.Id, decision.PolicyId);
    }

    [Fact]
    public void Denies_parameter_outside_the_allowlist()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("customerId")));

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { customerId = "c-42", includeSsn = true }), [policy]);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
        Assert.Contains("includeSsn", decision.Reason);
    }

    [Fact]
    public void Enforces_allowed_values()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("region", ["us", "eu"])));

        Assert.Equal(PolicyEffect.Allow, _engine.Evaluate(Agent, Tool(), Args(new { region = "eu" }), [policy]).Effect);
        Assert.Equal(PolicyEffect.Deny, _engine.Evaluate(Agent, Tool(), Args(new { region = "apac" }), [policy]).Effect);
    }

    [Fact]
    public void Compares_non_string_values_by_raw_json()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("limit", ["10", "50"])));

        Assert.Equal(PolicyEffect.Allow, _engine.Evaluate(Agent, Tool(), Args(new { limit = 10 }), [policy]).Effect);
        Assert.Equal(PolicyEffect.Deny, _engine.Evaluate(Agent, Tool(), Args(new { limit = 1000 }), [policy]).Effect);
    }

    [Fact]
    public void Holds_sensitive_tool_for_approval()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup"));

        var decision = _engine.Evaluate(Agent, Tool(sensitive: true), Args(new { }), [policy]);

        Assert.Equal(PolicyEffect.RequireApproval, decision.Effect);
    }

    [Fact]
    public void Holds_for_approval_when_the_grant_requires_it()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", requireApproval: true));

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { }), [policy]);

        Assert.Equal(PolicyEffect.RequireApproval, decision.Effect);
    }

    [Fact]
    public void Sensitive_tool_still_refuses_out_of_scope_parameters()
    {
        var policy = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("customerId")));

        var decision = _engine.Evaluate(Agent, Tool(sensitive: true), Args(new { other = 1 }), [policy]);

        Assert.Equal(PolicyEffect.Deny, decision.Effect);
    }

    [Fact]
    public void Another_matching_policy_can_permit_what_the_first_refuses()
    {
        var narrow = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("customerId")));
        var broad = PolicyFor("agent-1", Grant("crm.lookup", false, new ParameterRule("customerId"), new ParameterRule("fields")));

        var decision = _engine.Evaluate(Agent, Tool(), Args(new { customerId = "c-1", fields = "name" }), [narrow, broad]);

        Assert.Equal(PolicyEffect.Allow, decision.Effect);
        Assert.Equal(broad.Id, decision.PolicyId);
    }

    [Fact]
    public void IsGranted_reflects_enabled_policies_and_tools()
    {
        var policies = new[] { PolicyFor("agent-1", Grant("crm.lookup")) };

        Assert.True(_engine.IsGranted(Agent, Tool(), policies));
        Assert.False(_engine.IsGranted(Agent, Tool("billing.refund"), policies));
        Assert.False(_engine.IsGranted(Agent, Tool(enabled: false), policies));
    }
}
