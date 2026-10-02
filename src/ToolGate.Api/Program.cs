using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ToolGate.Api.Auth;
using ToolGate.Api.Endpoints;
using ToolGate.Api.Mcp;
using ToolGate.Api.OpenApi;
using ToolGate.Core.Approvals;
using ToolGate.Core.Gateway;
using ToolGate.Core.Invocation;
using ToolGate.Core.Persistence;
using ToolGate.Core.Policies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ToolGateDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("ToolGate")
        ?? throw new InvalidOperationException("Connection string 'ToolGate' is not configured.");
    var provider = builder.Configuration["Database:Provider"] ?? "PostgreSQL";
    switch (provider.ToLowerInvariant())
    {
        case "postgresql":
            options.UseNpgsql(connectionString);
            break;
        case "sqlserver":
            options.UseSqlServer(connectionString);
            break;
        default:
            throw new InvalidOperationException($"Unsupported Database:Provider '{provider}'. Use PostgreSQL or SqlServer.");
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPolicyEngine, PolicyEngine>();
builder.Services.AddHttpClient<IToolInvoker, HttpToolInvoker>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<GatewayService>();
builder.Services.AddScoped<ApprovalService>();

builder.Services.AddGatewayAuthentication(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "ToolGate API",
            Version = "v1",
            Description = "Policy-enforcing gateway between AI agents and internal APIs.",
        };
        return Task.CompletedTask;
    });
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services.AddMcpServer(options =>
        options.ServerInfo = new Implementation { Name = "ToolGate", Version = "0.1.0" })
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithListToolsHandler(McpToolHandlers.ListToolsAsync)
    .WithCallToolHandler(McpToolHandlers.CallToolAsync);

builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:EnsureCreated"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ToolGateDbContext>().Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ToolGate API v1"));
}

if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(app.Configuration["Auth:SigningKey"]))
{
    app.MapDevTokenEndpoints();
}

app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapAgentEndpoints();
app.MapAdminEndpoints();
app.MapApprovalEndpoints();
app.MapAuditEndpoints();
app.MapMcp("/mcp").RequireAuthorization();

app.Run();

public partial class Program;
