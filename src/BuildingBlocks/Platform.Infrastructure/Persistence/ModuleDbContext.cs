using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Outbox;
using Platform.SharedKernel.Domain;

namespace Platform.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for every module. Each module owns one PostgreSQL schema, its own migrations
/// history and its own outbox table. Cross-cutting rules applied here:
/// <list type="bullet">
/// <item>Named query filter <see cref="QueryFilters.Tenant"/> isolates tenant data.</item>
/// <item>Named query filter <see cref="QueryFilters.SoftDelete"/> hides soft-deleted rows.</item>
/// <item>PostgreSQL <c>xmin</c> is the optimistic concurrency token for aggregate roots.</item>
/// </list>
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    protected ModuleDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>PostgreSQL schema owned by the module.</summary>
    public abstract string Schema { get; }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Evaluated per query (EF parameterises context members), so a pooled or long-lived
    /// context always sees the current tenant. <see cref="Guid.Empty"/> matches nothing.
    /// </summary>
    protected Guid CurrentTenantId => _tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new NumberSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration(ownsTable: OwnsAuditTable));
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration(ownsTable: OwnsAuditTable));

        ApplyModuleConfigurations(modelBuilder);
        ApplyConventions(modelBuilder);
    }

    /// <summary>Only the audit module creates the shared audit table; others map it read/write only.</summary>
    protected virtual bool OwnsAuditTable => false;

    protected virtual void ApplyModuleConfigurations(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, t => t.Namespace?.StartsWith(GetType().Namespace!, StringComparison.Ordinal) ?? false);

    private void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => !e.IsOwned() && e.BaseType is null))
        {
            var clrType = entityType.ClrType;

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
                modelBuilder.Entity(clrType).HasIndex(nameof(ITenantOwned.TenantId));
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                ApplySoftDeleteFilterMethod.MakeGenericMethod(clrType).Invoke(null, [modelBuilder]);
            }

            if (typeof(AggregateRoot).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property<uint>("xmin")
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }

            if (typeof(Entity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }
        }
    }

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ApplySoftDeleteFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplySoftDeleteFilter), BindingFlags.Static | BindingFlags.NonPublic)!;

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantOwned
    {
        Expression<Func<TEntity, bool>> filter = e => e.TenantId == CurrentTenantId;
        modelBuilder.Entity<TEntity>().HasQueryFilter(QueryFilters.Tenant, filter);
    }

    private static void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ISoftDeletable
    {
        Expression<Func<TEntity, bool>> filter = e => !e.IsDeleted;
        modelBuilder.Entity<TEntity>().HasQueryFilter(QueryFilters.SoftDelete, filter);
    }
}

public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
