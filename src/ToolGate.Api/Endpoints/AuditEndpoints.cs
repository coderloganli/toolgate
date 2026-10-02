using Microsoft.EntityFrameworkCore;
using ToolGate.Api.Auth;
using ToolGate.Core.Auditing;
using ToolGate.Core.Domain;
using ToolGate.Core.Persistence;

namespace ToolGate.Api.Endpoints;

public sealed record AuditPage(IReadOnlyList<AuditRecord> Items, int Total, int Page, int PageSize);

/// <summary>Operator endpoint for querying the call audit trail.</summary>
public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", async (
                string? caller,
                string? tool,
                CallOutcome? outcome,
                string? approver,
                Guid? approvalRequestId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                int? page,
                int? pageSize,
                ToolGateDbContext db,
                CancellationToken ct) =>
            {
                var query = new AuditQuery(caller, tool, outcome, approver, approvalRequestId,
                    from?.ToUniversalTime(), to?.ToUniversalTime(), page ?? 1, pageSize ?? 50);
                var filtered = query.Apply(db.AuditRecords.AsNoTracking());
                var total = await filtered.CountAsync(ct);
                var items = await filtered.Skip(query.Skip).Take(query.Take).ToListAsync(ct);
                return new AuditPage(items, total, Math.Max(query.Page, 1), query.Take);
            })
            .RequireAuthorization(AuthenticationSetup.OperatorPolicy)
            .WithTags("Audit")
            .WithSummary("Queries the audit trail by caller, tool, outcome, approver, approval request and time range.");
    }
}
