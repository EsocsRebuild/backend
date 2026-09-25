using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Infrastructure.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    SoftDeleted,
}

/// <summary>
/// Immutable, append-only record of a data change. Written by the save interceptor in the
/// same transaction as the change, so the audit trail can never disagree with the data.
/// Stored in the shared <c>audit.audit_entries</c> table.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? TenantId { get; init; }
    public Guid? UserId { get; init; }
    public required string Module { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public AuditAction Action { get; init; }

    /// <summary>JSON object: { "Property": { "old": ..., "new": ... } }. Sensitive fields are redacted.</summary>
    public string? Changes { get; init; }

    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}

public sealed class AuditEntryConfiguration(bool ownsTable) : IEntityTypeConfiguration<AuditEntry>
{
    public const string Schema = "audit";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries", Schema, t =>
        {
            if (!ownsTable)
            {
                t.ExcludeFromMigrations();
            }
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Module).HasMaxLength(64);
        builder.Property(x => x.EntityType).HasMaxLength(128);
        builder.Property(x => x.EntityId).HasMaxLength(128);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Changes).HasColumnType("jsonb");
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.UserId);
    }
}
