using ToolGate.Core.Domain;

namespace ToolGate.Core.Auditing;

public sealed record AuditQuery(
    string? Caller = null,
    string? Tool = null,
    CallOutcome? Outcome = null,
    string? Approver = null,
    Guid? ApprovalRequestId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Page = 1,
    int PageSize = 50)
{
    public const int MaxPageSize = 200;

    public IQueryable<AuditRecord> Apply(IQueryable<AuditRecord> records)
    {
        if (!string.IsNullOrWhiteSpace(Caller))
        {
            records = records.Where(r => r.CallerSubject == Caller);
        }

        if (!string.IsNullOrWhiteSpace(Tool))
        {
            records = records.Where(r => r.ToolName == Tool);
        }

        if (Outcome is not null)
        {
            records = records.Where(r => r.Outcome == Outcome);
        }

        if (!string.IsNullOrWhiteSpace(Approver))
        {
            records = records.Where(r => r.ApproverSubject == Approver);
        }

        if (ApprovalRequestId is not null)
        {
            records = records.Where(r => r.ApprovalRequestId == ApprovalRequestId);
        }

        if (From is not null)
        {
            records = records.Where(r => r.Timestamp >= From);
        }

        if (To is not null)
        {
            records = records.Where(r => r.Timestamp < To);
        }

        return records.OrderByDescending(r => r.Timestamp);
    }

    public int Skip => (Math.Max(Page, 1) - 1) * Take;

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);
}
