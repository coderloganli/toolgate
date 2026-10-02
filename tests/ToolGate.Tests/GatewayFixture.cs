using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ToolGate.Core.Approvals;
using ToolGate.Core.Domain;
using ToolGate.Core.Gateway;
using ToolGate.Core.Invocation;
using ToolGate.Core.Persistence;
using ToolGate.Core.Policies;

namespace ToolGate.Tests;

internal sealed class RecordingInvoker : IToolInvoker
{
    public List<(string Tool, IReadOnlyDictionary<string, JsonElement> Arguments)> Calls { get; } = [];

    public InvocationResult NextResult { get; set; } = new(true, 200, """{"ok":true}""");

    public Task<InvocationResult> InvokeAsync(
        ToolDefinition tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        Calls.Add((tool.Name, arguments));
        return Task.FromResult(NextResult);
    }
}

/// <summary>Wires the gateway and approval services over an isolated in-memory database.</summary>
internal sealed class GatewayFixture : IDisposable
{
    public GatewayFixture()
    {
        var options = new DbContextOptionsBuilder<ToolGateDbContext>()
            .UseInMemoryDatabase($"toolgate-{Guid.NewGuid():N}")
            .Options;
        Db = new ToolGateDbContext(options);
        Gateway = new GatewayService(Db, new PolicyEngine(), Invoker, TimeProvider.System);
        Approvals = new ApprovalService(Db, new PolicyEngine(), Invoker, TimeProvider.System);
    }

    public ToolGateDbContext Db { get; }

    public RecordingInvoker Invoker { get; } = new();

    public GatewayService Gateway { get; }

    public ApprovalService Approvals { get; }

    public async Task<ToolDefinition> AddToolAsync(string name, bool sensitive = false)
    {
        var tool = new ToolDefinition { Name = name, EndpointUrl = $"https://internal.example/{name}", IsSensitive = sensitive };
        Db.Tools.Add(tool);
        await Db.SaveChangesAsync();
        return tool;
    }

    public async Task<Policy> AddPolicyAsync(string subject, string tool, params string[] parameters)
    {
        var policy = new Policy
        {
            Name = $"{subject}-{tool}",
            CallerSubject = subject,
            Grants = [new ToolGrant { ToolName = tool, Parameters = parameters.Select(p => new ParameterRule(p)).ToList() }],
        };
        Db.Policies.Add(policy);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return policy;
    }

    public static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(values))!;

    public void Dispose() => Db.Dispose();
}
