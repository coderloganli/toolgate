using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ToolGate.Api.Auth;

namespace ToolGate.Api.Endpoints;

public sealed record DevTokenRequest(string Subject, string? Name, string[]? Roles);

public sealed record DevTokenResponse(string AccessToken, DateTime ExpiresAt);

/// <summary>
/// Issues tokens signed with the local development key so the gateway can be exercised without an identity
/// provider. Mapped only in the Development environment when <c>Auth:SigningKey</c> is set.
/// </summary>
public static class DevTokenEndpoints
{
    public static void MapDevTokenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/token", (DevTokenRequest request, IOptions<GatewayAuthOptions> options) =>
            {
                var auth = options.Value;
                if (string.IsNullOrWhiteSpace(request.Subject))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.Subject)] = ["Required."],
                    });
                }

                var claims = new List<Claim> { new(auth.SubjectClaim, request.Subject) };
                if (!string.IsNullOrWhiteSpace(request.Name))
                {
                    claims.Add(new Claim(auth.NameClaim, request.Name));
                }

                claims.AddRange((request.Roles ?? []).Select(role => new Claim(auth.RoleClaim, role)));

                var expiresAt = DateTime.UtcNow.AddHours(8);
                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Issuer = auth.Issuer,
                    Audience = auth.Audience,
                    Expires = expiresAt,
                    SigningCredentials = new SigningCredentials(
                        AuthenticationSetup.CreateSigningKey(auth.SigningKey!), SecurityAlgorithms.HmacSha256),
                });

                return Results.Ok(new DevTokenResponse(token, expiresAt));
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Issues a development bearer token. Not available outside Development.")
            .Produces<DevTokenResponse>();
    }
}
