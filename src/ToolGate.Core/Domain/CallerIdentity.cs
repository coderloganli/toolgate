namespace ToolGate.Core.Domain;

/// <summary>
/// The authenticated caller as seen by the policy engine, mapped from validated token claims.
/// </summary>
public sealed record CallerIdentity(
    string Subject,
    string? DisplayName = null,
    string? ClientId = null,
    IReadOnlyCollection<string>? Roles = null);
