using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ToolGate.Core.Domain;

namespace ToolGate.Api.Auth;

public static class AuthenticationSetup
{
    public const string OperatorPolicy = "Operator";
    public const string AdminPolicy = "Admin";

    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(GatewayAuthOptions.SectionName);
        services.Configure<GatewayAuthOptions>(section);
        var options = section.Get<GatewayAuthOptions>() ?? new GatewayAuthOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                // Keep raw JWT claim names (sub, roles, azp) instead of the legacy WS-Federation URIs.
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = options.NameClaim,
                    RoleClaimType = options.RoleClaim,
                    ValidateAudience = !string.IsNullOrEmpty(options.Audience),
                    ValidAudience = options.Audience,
                };

                if (!string.IsNullOrEmpty(options.Authority))
                {
                    jwt.Authority = options.Authority;
                    jwt.Audience = options.Audience;
                }
                else if (!string.IsNullOrEmpty(options.SigningKey))
                {
                    jwt.TokenValidationParameters.IssuerSigningKey = CreateSigningKey(options.SigningKey);
                    jwt.TokenValidationParameters.ValidateIssuer = !string.IsNullOrEmpty(options.Issuer);
                    jwt.TokenValidationParameters.ValidIssuer = options.Issuer;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Configure Auth:Authority (OpenID Connect issuer) or, for local development, Auth:SigningKey.");
                }
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(OperatorPolicy, p => p.RequireAuthenticatedUser().RequireRole(options.OperatorRole))
            .AddPolicy(AdminPolicy, p => p.RequireAuthenticatedUser().RequireRole(options.AdminRole));

        return services;
    }

    public static SymmetricSecurityKey CreateSigningKey(string key) => new(Encoding.UTF8.GetBytes(key));

    /// <summary>Returns the caller identity for the current request, or null when the token lacks a subject.</summary>
    public static CallerIdentity? GetCaller(this HttpContext httpContext) =>
        CallerIdentityMapper.Map(
            httpContext.User,
            httpContext.RequestServices.GetRequiredService<IOptions<GatewayAuthOptions>>().Value);
}
