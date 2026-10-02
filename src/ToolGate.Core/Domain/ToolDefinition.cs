namespace ToolGate.Core.Domain;

/// <summary>
/// An internal service endpoint registered as an agent-callable tool.
/// </summary>
public class ToolDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Agent-facing tool name. Unique across the gateway.</summary>
    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Absolute URL of the internal service endpoint the gateway forwards approved calls to.</summary>
    public required string EndpointUrl { get; set; }

    /// <summary>HTTP method used to reach the endpoint. GET and DELETE send arguments as query parameters.</summary>
    public string HttpMethod { get; set; } = "POST";

    /// <summary>JSON Schema (object) describing the tool's arguments, advertised to agents.</summary>
    public string InputSchema { get; set; } = """{"type":"object"}""";

    /// <summary>Calls to a sensitive tool are always held for human approval.</summary>
    public bool IsSensitive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
