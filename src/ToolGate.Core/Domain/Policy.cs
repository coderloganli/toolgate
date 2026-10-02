namespace ToolGate.Core.Domain;

/// <summary>
/// Binds a caller identity to the set of tools it may call and the parameters it may pass.
/// </summary>
public class Policy
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>The caller identity (mapped from the token's subject claim) this policy applies to.</summary>
    public required string CallerSubject { get; set; }

    public bool IsEnabled { get; set; } = true;

    public List<ToolGrant> Grants { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Grants access to one tool, restricted to an allowlist of parameters.
/// </summary>
public class ToolGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PolicyId { get; set; }

    public required string ToolName { get; set; }

    /// <summary>Parameters the caller may pass. Any argument not listed here is refused.</summary>
    public List<ParameterRule> Parameters { get; set; } = [];

    /// <summary>Hold calls under this grant for approval even when the tool itself is not sensitive.</summary>
    public bool RequireApproval { get; set; }
}

/// <summary>
/// An allowlisted parameter. When <see cref="AllowedValues"/> is empty, any value is accepted.
/// </summary>
public sealed record ParameterRule(string Name, IReadOnlyList<string>? AllowedValues = null);
