using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ToolGate.Core.Domain;

namespace ToolGate.Core.Persistence;

/// <summary>
/// Persistence for tools, policies, approvals and the audit trail. The model uses only provider-neutral
/// mappings so the same context runs against SQL Server and PostgreSQL.
/// </summary>
public class ToolGateDbContext(DbContextOptions<ToolGateDbContext> options) : DbContext(options)
{
    public DbSet<ToolDefinition> Tools => Set<ToolDefinition>();

    public DbSet<Policy> Policies => Set<Policy>();

    public DbSet<ToolGrant> ToolGrants => Set<ToolGrant>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ToolDefinition>(e =>
        {
            e.ToTable("tools");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Description).HasMaxLength(2048);
            e.Property(t => t.EndpointUrl).HasMaxLength(2048).IsRequired();
            e.Property(t => t.HttpMethod).HasMaxLength(16).IsRequired();
            e.Property(t => t.InputSchema).IsRequired();
        });

        modelBuilder.Entity<Policy>(e =>
        {
            e.ToTable("policies");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(128).IsRequired();
            e.HasIndex(p => p.Name).IsUnique();
            e.Property(p => p.CallerSubject).HasMaxLength(256).IsRequired();
            e.HasIndex(p => p.CallerSubject);
            e.HasMany(p => p.Grants).WithOne().HasForeignKey(g => g.PolicyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ToolGrant>(e =>
        {
            e.ToTable("tool_grants");
            e.HasKey(g => g.Id);
            e.Property(g => g.ToolName).HasMaxLength(128).IsRequired();
            e.HasIndex(g => new { g.PolicyId, g.ToolName }).IsUnique();
            e.Property(g => g.Parameters)
                .HasConversion(
                    v => SerializeRules(v),
                    v => DeserializeRules(v),
                    new ValueComparer<List<ParameterRule>>(
                        (a, b) => SerializeRules(a) == SerializeRules(b),
                        v => SerializeRules(v).GetHashCode(),
                        v => DeserializeRules(SerializeRules(v))))
                .HasColumnName("parameters_json")
                .IsRequired();
        });

        modelBuilder.Entity<ApprovalRequest>(e =>
        {
            e.ToTable("approval_requests");
            e.HasKey(a => a.Id);
            e.Property(a => a.CallerSubject).HasMaxLength(256).IsRequired();
            e.Property(a => a.CallerName).HasMaxLength(256);
            e.Property(a => a.ClientId).HasMaxLength(256);
            e.Property(a => a.ToolName).HasMaxLength(128).IsRequired();
            e.Property(a => a.Channel).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.DecisionReason).HasMaxLength(1024);
            e.Property(a => a.DecidedBy).HasMaxLength(256);
            e.Property(a => a.DecisionComment).HasMaxLength(1024);
            e.Property(a => a.ConcurrencyStamp).IsConcurrencyToken();
            e.HasIndex(a => new { a.Status, a.CreatedAt });
        });

        modelBuilder.Entity<AuditRecord>(e =>
        {
            e.ToTable("audit_records");
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(32);
            e.Property(a => a.CallerSubject).HasMaxLength(256).IsRequired();
            e.Property(a => a.CallerName).HasMaxLength(256);
            e.Property(a => a.ClientId).HasMaxLength(256);
            e.Property(a => a.Channel).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.ToolName).HasMaxLength(128).IsRequired();
            e.Property(a => a.Decision).HasConversion<string>().HasMaxLength(32);
            e.Property(a => a.DecisionReason).HasMaxLength(1024);
            e.Property(a => a.ApproverSubject).HasMaxLength(256);
            e.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(32);
            e.Property(a => a.CorrelationId).HasMaxLength(128);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => new { a.CallerSubject, a.Timestamp });
            e.HasIndex(a => new { a.ToolName, a.Timestamp });
            e.HasIndex(a => a.ApprovalRequestId);
        });
    }

    private static string SerializeRules(List<ParameterRule> rules) => JsonSerializer.Serialize(rules);

    private static List<ParameterRule> DeserializeRules(string json) =>
        JsonSerializer.Deserialize<List<ParameterRule>>(json) ?? [];
}
