using System.Security.Claims;
using ToolGate.Core.Domain;

namespace ToolGate.Api.Auth;

/// <summary>Maps the claims of a validated JWT to the caller identity the policy engine checks.</summary>
public static class CallerIdentityMapper
{
    private static readonly string[] ClientIdClaims = ["azp", "appid", "client_id"];

    public static CallerIdentity? Map(ClaimsPrincipal? principal, GatewayAuthOptions options)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = principal.FindFirst(options.SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var name = principal.FindFirst(options.NameClaim)?.Value ?? principal.FindFirst("preferred_username")?.Value;
        var clientId = ClientIdClaims.Select(c => principal.FindFirst(c)?.Value).FirstOrDefault(v => v is not null);
        var roles = principal.FindAll(options.RoleClaim).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();

        return new CallerIdentity(subject, name, clientId, roles);
    }
}
