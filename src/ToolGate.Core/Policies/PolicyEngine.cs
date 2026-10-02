using System.Text.Json;
using ToolGate.Core.Domain;

namespace ToolGate.Core.Policies;

/// <summary>
/// Default-deny policy engine. A call is allowed only when an enabled policy bound to the caller grants the tool
/// and every argument is on that grant's parameter allowlist. Sensitive tools, and grants that require approval,
/// turn an allow into a hold for human approval.
/// </summary>
public sealed class PolicyEngine : IPolicyEngine
{
    public PolicyDecision Evaluate(
        CallerIdentity caller,
        ToolDefinition? tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        IEnumerable<Policy> policies)
    {
        if (tool is null)
        {
            return PolicyDecision.Deny("Tool is not registered.");
        }

        if (!tool.IsEnabled)
        {
            return PolicyDecision.Deny($"Tool '{tool.Name}' is disabled.");
        }

        var candidates = MatchingGrants(caller, tool, policies).ToList();
        if (candidates.Count == 0)
        {
            return PolicyDecision.Deny($"No policy grants caller '{caller.Subject}' access to tool '{tool.Name}'.");
        }

        PolicyDecision? firstViolation = null;
        foreach (var (policy, grant) in candidates)
        {
            var violation = FindParameterViolation(grant, arguments);
            if (violation is not null)
            {
                firstViolation ??= PolicyDecision.Deny(violation, policy.Id);
                continue;
            }

            if (tool.IsSensitive)
            {
                return new PolicyDecision(PolicyEffect.RequireApproval,
                    $"Tool '{tool.Name}' is sensitive; held for approval under policy '{policy.Name}'.", policy.Id);
            }

            if (grant.RequireApproval)
            {
                return new PolicyDecision(PolicyEffect.RequireApproval,
                    $"Policy '{policy.Name}' requires approval for tool '{tool.Name}'.", policy.Id);
            }

            return new PolicyDecision(PolicyEffect.Allow, $"Allowed by policy '{policy.Name}'.", policy.Id);
        }

        return firstViolation!;
    }

    public bool IsGranted(CallerIdentity caller, ToolDefinition tool, IEnumerable<Policy> policies) =>
        tool.IsEnabled && MatchingGrants(caller, tool, policies).Any();

    private static IEnumerable<(Policy Policy, ToolGrant Grant)> MatchingGrants(
        CallerIdentity caller, ToolDefinition tool, IEnumerable<Policy> policies) =>
        policies
            .Where(p => p.IsEnabled && string.Equals(p.CallerSubject, caller.Subject, StringComparison.Ordinal))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .SelectMany(p => p.Grants
                .Where(g => string.Equals(g.ToolName, tool.Name, StringComparison.Ordinal))
                .Select(g => (p, g)));

    private static string? FindParameterViolation(ToolGrant grant, IReadOnlyDictionary<string, JsonElement> arguments)
    {
        foreach (var (name, value) in arguments)
        {
            var rule = grant.Parameters.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
            if (rule is null)
            {
                return $"Parameter '{name}' is not on the allowlist for tool '{grant.ToolName}'.";
            }

            if (rule.AllowedValues is { Count: > 0 } allowed && !allowed.Contains(ToComparable(value), StringComparer.Ordinal))
            {
                return $"Value for parameter '{name}' is not allowed for tool '{grant.ToolName}'.";
            }
        }

        return null;
    }

    private static string ToComparable(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
}
