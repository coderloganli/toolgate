using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ToolGate.Api.Auth;
using ToolGate.Core.Domain;
using ToolGate.Core.Persistence;

namespace ToolGate.Api.Endpoints;

public sealed record ToolInput(
    string Name,
    string? Description,
    string EndpointUrl,
    string? HttpMethod,
    JsonElement? InputSchema,
    bool IsSensitive,
    bool IsEnabled = true);

public sealed record ToolGrantInput(string ToolName, List<ParameterRule>? Parameters, bool RequireApproval);

public sealed record PolicyInput(string Name, string CallerSubject, bool IsEnabled, List<ToolGrantInput> Grants);

/// <summary>Tool registration and policy management.</summary>
public static partial class AdminEndpoints
{
    private static readonly string[] AllowedMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex ToolNamePattern();

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(AuthenticationSetup.AdminPolicy);

        var tools = admin.MapGroup("/tools").WithTags("Admin: tools");
        tools.MapGet("/", async (ToolGateDbContext db, CancellationToken ct) =>
            await db.Tools.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct));

        tools.MapGet("/{id:guid}", async (Guid id, ToolGateDbContext db, CancellationToken ct) =>
            await db.Tools.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) is { } tool
                ? Results.Ok(tool)
                : Results.NotFound());

        tools.MapPost("/", async (ToolInput input, ToolGateDbContext db, CancellationToken ct) =>
        {
            if (ValidateTool(input) is { } errors)
            {
                return Results.ValidationProblem(errors);
            }

            if (await db.Tools.AnyAsync(t => t.Name == input.Name, ct))
            {
                return Results.Conflict($"Tool '{input.Name}' already exists.");
            }

            var tool = new ToolDefinition { Name = input.Name, EndpointUrl = input.EndpointUrl };
            Apply(tool, input);
            db.Tools.Add(tool);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/tools/{tool.Id}", tool);
        });

        tools.MapPut("/{id:guid}", async (Guid id, ToolInput input, ToolGateDbContext db, CancellationToken ct) =>
        {
            if (ValidateTool(input) is { } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var tool = await db.Tools.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tool is null)
            {
                return Results.NotFound();
            }

            if (tool.Name != input.Name)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(ToolInput.Name)] = ["Tool names are immutable because policies and audit records refer to them."],
                });
            }

            Apply(tool, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(tool);
        });

        tools.MapDelete("/{id:guid}", async (Guid id, ToolGateDbContext db, CancellationToken ct) =>
        {
            var tool = await db.Tools.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tool is null)
            {
                return Results.NotFound();
            }

            db.Tools.Remove(tool);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        var policies = admin.MapGroup("/policies").WithTags("Admin: policies");
        policies.MapGet("/", async (ToolGateDbContext db, CancellationToken ct) =>
            await db.Policies.AsNoTracking().Include(p => p.Grants).OrderBy(p => p.Name).ToListAsync(ct));

        policies.MapGet("/{id:guid}", async (Guid id, ToolGateDbContext db, CancellationToken ct) =>
            await db.Policies.AsNoTracking().Include(p => p.Grants).FirstOrDefaultAsync(p => p.Id == id, ct) is { } policy
                ? Results.Ok(policy)
                : Results.NotFound());

        policies.MapPost("/", async (PolicyInput input, ToolGateDbContext db, CancellationToken ct) =>
        {
            if (ValidatePolicy(input) is { } errors)
            {
                return Results.ValidationProblem(errors);
            }

            if (await db.Policies.AnyAsync(p => p.Name == input.Name, ct))
            {
                return Results.Conflict($"Policy '{input.Name}' already exists.");
            }

            var policy = new Policy { Name = input.Name, CallerSubject = input.CallerSubject };
            Apply(policy, input);
            db.Policies.Add(policy);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/policies/{policy.Id}", policy);
        });

        policies.MapPut("/{id:guid}", async (Guid id, PolicyInput input, ToolGateDbContext db, CancellationToken ct) =>
        {
            if (ValidatePolicy(input) is { } errors)
            {
                return Results.ValidationProblem(errors);
            }

            var policy = await db.Policies.Include(p => p.Grants).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (policy is null)
            {
                return Results.NotFound();
            }

            policy.Name = input.Name;
            policy.CallerSubject = input.CallerSubject;
            policy.UpdatedAt = DateTimeOffset.UtcNow;
            db.ToolGrants.RemoveRange(policy.Grants);
            policy.Grants.Clear();
            Apply(policy, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(policy);
        });

        policies.MapDelete("/{id:guid}", async (Guid id, ToolGateDbContext db, CancellationToken ct) =>
        {
            var policy = await db.Policies.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (policy is null)
            {
                return Results.NotFound();
            }

            db.Policies.Remove(policy);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static void Apply(ToolDefinition tool, ToolInput input)
    {
        tool.Description = input.Description ?? string.Empty;
        tool.EndpointUrl = input.EndpointUrl;
        tool.HttpMethod = (input.HttpMethod ?? "POST").ToUpperInvariant();
        tool.InputSchema = input.InputSchema?.GetRawText() ?? """{"type":"object"}""";
        tool.IsSensitive = input.IsSensitive;
        tool.IsEnabled = input.IsEnabled;
    }

    private static void Apply(Policy policy, PolicyInput input)
    {
        policy.IsEnabled = input.IsEnabled;
        foreach (var grant in input.Grants)
        {
            policy.Grants.Add(new ToolGrant
            {
                PolicyId = policy.Id,
                ToolName = grant.ToolName,
                Parameters = grant.Parameters ?? [],
                RequireApproval = grant.RequireApproval,
            });
        }
    }

    internal static Dictionary<string, string[]>? ValidateTool(ToolInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(input.Name) || !ToolNamePattern().IsMatch(input.Name))
        {
            errors[nameof(input.Name)] = ["Use 1-128 characters: letters, digits, '_', '-' or '.'."];
        }

        if (!Uri.TryCreate(input.EndpointUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors[nameof(input.EndpointUrl)] = ["Must be an absolute http or https URL."];
        }

        if (input.HttpMethod is { } method && !AllowedMethods.Contains(method.ToUpperInvariant()))
        {
            errors[nameof(input.HttpMethod)] = [$"Must be one of {string.Join(", ", AllowedMethods)}."];
        }

        if (input.InputSchema is { } schema && schema.ValueKind != JsonValueKind.Object)
        {
            errors[nameof(input.InputSchema)] = ["Must be a JSON Schema object."];
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static Dictionary<string, string[]>? ValidatePolicy(PolicyInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            errors[nameof(input.Name)] = ["Required."];
        }

        if (string.IsNullOrWhiteSpace(input.CallerSubject))
        {
            errors[nameof(input.CallerSubject)] = ["Required."];
        }

        var grants = input.Grants ?? [];
        if (grants.GroupBy(g => g.ToolName, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            errors[nameof(input.Grants)] = ["Each tool may be granted only once per policy."];
        }

        if (grants.Any(g => string.IsNullOrWhiteSpace(g.ToolName)))
        {
            errors[nameof(input.Grants)] = ["Every grant needs a tool name."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
