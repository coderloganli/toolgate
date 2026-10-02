using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ToolGate.Api.Auth;
using ToolGate.Api.Endpoints;
using ToolGate.Core.Auditing;
using ToolGate.Core.Domain;
using ToolGate.Core.Invocation;

namespace ToolGate.Tests;

public class CallerIdentityMapperTests
{
    private static readonly GatewayAuthOptions Options = new();

    [Fact]
    public void Maps_subject_name_client_and_roles()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        new[]
        {
            new Claim("sub", "agent-1"),
            new Claim("name", "Support agent"),
            new Claim("azp", "client-app"),
            new Claim("roles", "ToolGate.Operator"),
            new Claim("roles", "ToolGate.Admin"),
        }, authenticationType: "Bearer"));

        var caller = CallerIdentityMapper.Map(principal, Options);

        Assert.NotNull(caller);
        Assert.Equal("agent-1", caller.Subject);
        Assert.Equal("Support agent", caller.DisplayName);
        Assert.Equal("client-app", caller.ClientId);
        Assert.Equal(new[] { "ToolGate.Operator", "ToolGate.Admin" }, caller.Roles!);
    }

    [Fact]
    public void Returns_null_for_unauthenticated_or_subjectless_principals()
    {
        Assert.Null(CallerIdentityMapper.Map(new ClaimsPrincipal(new ClaimsIdentity()), Options));
        Assert.Null(CallerIdentityMapper.Map(
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("name", "x") }, "Bearer")), Options));
    }

    [Fact]
    public void Uses_the_configured_subject_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "object-id") }, "Bearer"));

        var caller = CallerIdentityMapper.Map(principal, new GatewayAuthOptions { SubjectClaim = "oid" });

        Assert.Equal("object-id", caller?.Subject);
    }
}

public class HttpToolInvokerTests
{
    [Fact]
    public void Get_requests_carry_arguments_in_the_query_string()
    {
        var uri = HttpToolInvoker.BuildUri(
            "https://crm.internal/customers?v=2", HttpMethod.Get, GatewayFixture.Args(new { id = "a b", limit = 5 }));

        Assert.Equal("https://crm.internal/customers?v=2&id=a%20b&limit=5", uri.AbsoluteUri);
    }

    [Fact]
    public void Post_requests_leave_the_url_unchanged()
    {
        var uri = HttpToolInvoker.BuildUri("https://crm.internal/customers", HttpMethod.Post, GatewayFixture.Args(new { id = 1 }));

        Assert.Equal("https://crm.internal/customers", uri.AbsoluteUri);
    }
}

public class AuditQueryTests
{
    [Fact]
    public async Task Filters_by_caller_and_outcome_newest_first()
    {
        using var f = new GatewayFixture();
        var now = DateTimeOffset.UtcNow;
        f.Db.AuditRecords.AddRange(
            new AuditRecord { CallerSubject = "a", ToolName = "t", Outcome = CallOutcome.Executed, Timestamp = now.AddMinutes(-2) },
            new AuditRecord { CallerSubject = "a", ToolName = "t", Outcome = CallOutcome.Executed, Timestamp = now },
            new AuditRecord { CallerSubject = "a", ToolName = "t", Outcome = CallOutcome.Refused, Timestamp = now },
            new AuditRecord { CallerSubject = "b", ToolName = "t", Outcome = CallOutcome.Executed, Timestamp = now });
        await f.Db.SaveChangesAsync();

        var query = new AuditQuery(Caller: "a", Outcome: CallOutcome.Executed);
        var results = await query.Apply(f.Db.AuditRecords).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Timestamp > results[1].Timestamp);
    }

    [Fact]
    public void Clamps_paging()
    {
        var query = new AuditQuery(Page: 0, PageSize: 10_000);

        Assert.Equal(0, query.Skip);
        Assert.Equal(AuditQuery.MaxPageSize, query.Take);
    }
}

public class AdminValidationTests
{
    [Fact]
    public void Rejects_invalid_tool_definitions()
    {
        var errors = AdminEndpoints.ValidateTool(new ToolInput("bad name!", null, "not-a-url", "TRACE", null, false));

        Assert.NotNull(errors);
        Assert.Contains(nameof(ToolInput.Name), errors.Keys);
        Assert.Contains(nameof(ToolInput.EndpointUrl), errors.Keys);
        Assert.Contains(nameof(ToolInput.HttpMethod), errors.Keys);
    }

    [Fact]
    public void Rejects_duplicate_grants_for_the_same_tool()
    {
        var errors = AdminEndpoints.ValidatePolicy(new PolicyInput("p", "agent-1", true,
        [
            new ToolGrantInput("crm.lookup", null, false),
            new ToolGrantInput("crm.lookup", null, true),
        ]));

        Assert.NotNull(errors);
        Assert.Contains(nameof(PolicyInput.Grants), errors.Keys);
    }
}
