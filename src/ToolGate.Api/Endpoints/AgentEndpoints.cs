using System.Text.Json;
using ToolGate.Api.Auth;
using ToolGate.Core.Domain;
using ToolGate.Core.Gateway;

namespace ToolGate.Api.Endpoints;

public sealed record ToolSummary(string Name, string Description, JsonElement InputSchema, bool IsSensitive);

public sealed record ToolCallBody(Dictionary<string, JsonElement>? Arguments);

public sealed record ToolCallResponse(
    ToolCallStatus Status,
    string Message,
    Guid AuditId,
    Guid? ApprovalRequestId,
    int? UpstreamStatusCode,
    string? Body);

/// <summary>The agent-facing REST surface: tool discovery and policy-checked tool calls.</summary>
public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", (HttpContext http) =>
                http.GetCaller() is { } caller ? Results.Ok(caller) : Results.Unauthorized())
            .RequireAuthorization()
            .WithTags("Agent")
            .WithSummary("Returns the caller identity mapped from the bearer token.")
            .Produces<CallerIdentity>();

        var tools = app.MapGroup("/api/tools").RequireAuthorization().WithTags("Agent");

        tools.MapGet("/", async (HttpContext http, GatewayService gateway, CancellationToken ct) =>
            {
                if (http.GetCaller() is not { } caller)
                {
                    return Results.Unauthorized();
                }

                var granted = await gateway.ListGrantedToolsAsync(caller, ct);
                return Results.Ok(granted.Select(ToSummary));
            })
            .WithSummary("Lists the tools the caller is granted by policy.")
            .Produces<IEnumerable<ToolSummary>>();

        tools.MapPost("/{name}/calls", async (string name, ToolCallBody? body, HttpContext http, GatewayService gateway, CancellationToken ct) =>
            {
                if (http.GetCaller() is not { } caller)
                {
                    return Results.Unauthorized();
                }

                var request = new ToolCallRequest(
                    caller,
                    name,
                    body?.Arguments ?? new Dictionary<string, JsonElement>(),
                    CallChannel.Rest,
                    http.TraceIdentifier);
                var result = await gateway.CallAsync(request, ct);
                var response = new ToolCallResponse(result.Status, result.Message, result.AuditId,
                    result.ApprovalRequestId, result.UpstreamStatusCode, result.Body);

                return Results.Json(response, statusCode: result.Status switch
                {
                    ToolCallStatus.Executed => StatusCodes.Status200OK,
                    ToolCallStatus.PendingApproval => StatusCodes.Status202Accepted,
                    ToolCallStatus.Refused => StatusCodes.Status403Forbidden,
                    ToolCallStatus.UnknownTool => StatusCodes.Status404NotFound,
                    _ => StatusCodes.Status502BadGateway,
                });
            })
            .WithSummary("Calls a tool. The call is checked against policy, audited, and either executed, refused or held for approval.")
            .Produces<ToolCallResponse>(StatusCodes.Status200OK)
            .Produces<ToolCallResponse>(StatusCodes.Status202Accepted)
            .Produces<ToolCallResponse>(StatusCodes.Status403Forbidden)
            .Produces<ToolCallResponse>(StatusCodes.Status404NotFound)
            .Produces<ToolCallResponse>(StatusCodes.Status502BadGateway);
    }

    internal static ToolSummary ToSummary(ToolDefinition tool) =>
        new(tool.Name, tool.Description, ParseSchema(tool.InputSchema), tool.IsSensitive);

    internal static JsonElement ParseSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }
}
