namespace ToolGate.Api.Auth;

/// <summary>
/// OAuth 2.0 bearer token settings. Production deployments set <see cref="Authority"/> (for example a Microsoft
/// Entra ID tenant) so signing keys come from OpenID Connect discovery. <see cref="SigningKey"/> is a symmetric
/// key for local development only.
/// </summary>
public sealed class GatewayAuthOptions
{
    public const string SectionName = "Auth";

    public string? Authority { get; set; }

    public string? Audience { get; set; }

    public string? Issuer { get; set; }

    public string? SigningKey { get; set; }

    /// <summary>Claim that carries the caller identity bound by policies.</summary>
    public string SubjectClaim { get; set; } = "sub";

    public string NameClaim { get; set; } = "name";

    public string RoleClaim { get; set; } = "roles";

    /// <summary>Role allowed to clear approvals and query the audit trail.</summary>
    public string OperatorRole { get; set; } = "ToolGate.Operator";

    /// <summary>Role allowed to register tools and manage policies.</summary>
    public string AdminRole { get; set; } = "ToolGate.Admin";
}
