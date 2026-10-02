using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ToolGate.Api.Auth;
using ToolGate.Api.Endpoints;
using ToolGate.Core.Domain;
using ToolGate.Core.Gateway;

namespace ToolGate.Api.Mcp;

/// <summary>
/// Exposes the gateway's tool surface over MCP. Tools are registered dynamically through the SDK's
/// <c>tools/list</c> and <c>tools/call</c> handlers, so each caller sees only the tools its policies grant and
/// every call goes through the same policy check and audit trail as the REST API.
/// </summary>
public static class McpToolHandlers
{
    public static async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken)
    {
        var (gateway, caller) = Resolve(context);
        var tools = await gateway.ListGrantedToolsAsync(caller, cancellationToken);
        return new ListToolsResult { Tools = tools.Select(ToMcpTool).ToList() };
    }

    public static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        var (gateway, caller) = Resolve(context);
        var parameters = context.Params
            ?? throw new McpProtocolException("Missing tools/call parameters.", McpErrorCode.InvalidParams);
        var arguments = parameters.Arguments is null
            ? new Dictionary<string, JsonElement>()
            : new Dictionary<string, JsonElement>(parameters.Arguments);

        var result = await gateway.CallAsync(
            new ToolCallRequest(caller, parameters.Name, arguments, CallChannel.Mcp), cancellationToken);

        return result.Status switch
        {
            // Per the MCP tools spec, an unknown tool is a protocol error rather than a tool execution error.
            ToolCallStatus.UnknownTool => throw new McpProtocolException(
                $"Unknown tool: '{parameters.Name}'", McpErrorCode.InvalidParams),
            ToolCallStatus.Executed => TextResult(result.Body ?? string.Empty, isError: false),
            ToolCallStatus.Failed => TextResult(result.Body ?? result.Message, isError: true),
            ToolCallStatus.Refused => TextResult($"Refused by policy: {result.Message}", isError: true),
            _ => PendingResult(result),
        };
    }

    internal static Tool ToMcpTool(ToolDefinition tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = AgentEndpoints.ParseSchema(tool.InputSchema),
    };

    private static (GatewayService Gateway, CallerIdentity Caller) Resolve<TParams>(RequestContext<TParams> context)
    {
        var services = context.Services
            ?? throw new McpException("Request services are unavailable.");
        var options = services.GetRequiredService<IOptions<GatewayAuthOptions>>().Value;
        var caller = CallerIdentityMapper.Map(context.User, options)
            ?? throw new McpProtocolException("The bearer token does not identify a caller.", McpErrorCode.InvalidRequest);
        return (services.GetRequiredService<GatewayService>(), caller);
    }

    private static CallToolResult TextResult(string text, bool isError) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = isError,
    };

    private static CallToolResult PendingResult(ToolCallResult result)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "pending_approval",
            approvalRequestId = result.ApprovalRequestId,
            message = result.Message,
        });

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = payload.GetRawText() }],
            StructuredContent = payload,
            IsError = false,
        };
    }
}
