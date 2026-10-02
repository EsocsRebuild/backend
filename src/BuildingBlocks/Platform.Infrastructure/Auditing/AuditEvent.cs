using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Application.Abstractions;

namespace Platform.Infrastructure.Auditing;

/// <summary>A business-level activity record (<c>audit.audit_events</c>), shown in the portal's audit log.</summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? TenantId { get; init; }
    public required string Action { get; init; }
    public required string Summary { get; init; }
    public AuditSeverity Severity { get; init; }
    public Guid? ActorId { get; init; }
    public string? ActorName { get; init; }
    public string? ActorEmail { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? TargetLabel { get; init; }

    /// <summary>JSON: { "field": { "from": …, "to": … } }.</summary>
    public string? Changes { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? RequestId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AuditEventConfiguration(bool ownsTable) : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events", AuditEntryConfiguration.Schema, t =>
        {
            if (!ownsTable)
            {
                t.ExcludeFromMigrations();
            }
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(64);
        builder.Property(x => x.Summary).HasMaxLength(500);
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ActorName).HasMaxLength(200);
        builder.Property(x => x.ActorEmail).HasMaxLength(256);
        builder.Property(x => x.TargetType).HasMaxLength(64);
        builder.Property(x => x.TargetId).HasMaxLength(64);
        builder.Property(x => x.TargetLabel).HasMaxLength(200);
        builder.Property(x => x.Changes).HasColumnType("jsonb");
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.RequestId).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.Severity, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.ActorId, x.CreatedAt });
    }
}

/// <summary>Persists audit events that have no accompanying data change (implemented by the Identity module).</summary>
public interface IAuditEventStore
{
    Task SaveAsync(IReadOnlyCollection<AuditEvent> events, CancellationToken cancellationToken);
}
