using System.Text.Json;
using ToolGate.Core.Domain;

namespace ToolGate.Core.Invocation;

public sealed record InvocationResult(bool IsSuccess, int? StatusCode, string Body);

/// <summary>Forwards an authorized call to the internal service behind a tool.</summary>
public interface IToolInvoker
{
    Task<InvocationResult> InvokeAsync(
        ToolDefinition tool,
        IReadOnlyDictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken);
}
