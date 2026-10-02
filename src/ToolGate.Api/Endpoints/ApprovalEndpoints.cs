using ToolGate.Api.Auth;
using ToolGate.Core.Approvals;
using ToolGate.Core.Domain;

namespace ToolGate.Api.Endpoints;

public sealed record DecisionBody(string? Comment);

/// <summary>Operator endpoints for the human-approval queue.</summary>
public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var approvals = app.MapGroup("/api/approvals")
            .RequireAuthorization(AuthenticationSetup.OperatorPolicy)
            .WithTags("Approvals");

        approvals.MapGet("/", async (ApprovalStatus? status, ApprovalService service, CancellationToken ct) =>
                await service.ListAsync(status, ct))
            .WithSummary("Lists approval requests, newest first. Filter with ?status=Pending.");

        approvals.MapPost("/{id:guid}/approve", async (Guid id, DecisionBody? body, HttpContext http, ApprovalService service, CancellationToken ct) =>
                http.GetCaller() is { } approver
                    ? ToHttpResult(await service.ApproveAsync(id, approver, body?.Comment, ct))
                    : Results.Unauthorized())
            .WithSummary("Approves a held call and executes it, after re-checking the policy.")
            .Produces<ApprovalRequest>();

        approvals.MapPost("/{id:guid}/reject", async (Guid id, DecisionBody? body, HttpContext http, ApprovalService service, CancellationToken ct) =>
                http.GetCaller() is { } approver
                    ? ToHttpResult(await service.RejectAsync(id, approver, body?.Comment, ct))
                    : Results.Unauthorized())
            .WithSummary("Rejects a held call without executing it.")
            .Produces<ApprovalRequest>();
    }

    private static IResult ToHttpResult(ApprovalResult result) => result.Kind switch
    {
        ApprovalResultKind.Completed => Results.Ok(result.Request),
        ApprovalResultKind.NotFound => Results.NotFound(),
        ApprovalResultKind.SelfApprovalForbidden => Results.Problem(result.Message, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(result.Message, statusCode: StatusCodes.Status409Conflict),
    };
}
