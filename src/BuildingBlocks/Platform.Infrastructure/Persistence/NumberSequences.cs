using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Gap-tolerant, per-tenant counter used for human-readable numbers (member numbers, receipts).
/// Every module schema gets its own <c>number_sequences</c> table.
/// </summary>
public sealed class NumberSequence
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public long NextValue { get; set; }
}

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        builder.ToTable("number_sequences");
        builder.HasKey(x => new { x.TenantId, x.Name });
        builder.Property(x => x.Name).HasMaxLength(64);
    }
}

public static class NumberSequenceExtensions
{
    /// <summary>
    /// Atomically reserves the next value (single round trip, row-level lock, safe under concurrency).
    /// Values are never reused; a rolled-back transaction may leave a gap, which is acceptable for display numbers.
    /// </summary>
    public static async Task<long> NextNumberAsync(this ModuleDbContext db, Guid tenantId, string name, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002 // Schema is a module constant, values are parameterised.
        var values = await db.Database.SqlQueryRaw<long>($$"""
            INSERT INTO "{{db.Schema}}".number_sequences (tenant_id, name, next_value)
            VALUES ({0}, {1}, 2)
            ON CONFLICT (tenant_id, name) DO UPDATE SET next_value = number_sequences.next_value + 1
            RETURNING next_value - 1 AS "Value"
            """, tenantId, name).ToListAsync(cancellationToken);
#pragma warning restore EF1002
        return values[0];
    }
}
