using System.Text.Json;
using ToolGate.Core.Domain;

namespace ToolGate.Core.Policies;

public interface IPolicyEngine
{
    /// <summary>
    /// Decides whether <paramref name="caller"/> may call <paramref name="tool"/> with <paramref name="arguments"/>.
    /// </summary>
    PolicyDecision Evaluate(
        CallerIdentity caller,
        ToolDefinition? tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        IEnumerable<Policy> policies);

    /// <summary>True when an enabled policy for <paramref name="caller"/> grants access to an enabled <paramref name="tool"/>.</summary>
    bool IsGranted(CallerIdentity caller, ToolDefinition tool, IEnumerable<Policy> policies);
}
