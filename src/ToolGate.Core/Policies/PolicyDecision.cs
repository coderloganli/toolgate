using ToolGate.Core.Domain;

namespace ToolGate.Core.Policies;

public sealed record PolicyDecision(PolicyEffect Effect, string Reason, Guid? PolicyId = null)
{
    public static PolicyDecision Deny(string reason, Guid? policyId = null) => new(PolicyEffect.Deny, reason, policyId);
}
