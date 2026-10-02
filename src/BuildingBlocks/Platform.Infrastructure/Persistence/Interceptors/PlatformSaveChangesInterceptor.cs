using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Outbox;
using Platform.SharedKernel.Domain;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Single interceptor enforcing the platform's write rules, in order:
/// 1. tenant stamping + cross-tenant write protection,
/// 2. soft delete conversion,
/// 3. created/updated metadata,
/// 4. audit trail (same transaction),
/// 5. domain events → outbox (same transaction).
/// </summary>
public sealed class PlatformSaveChangesInterceptor(
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IRequestInfo requestInfo,
    AuditLog auditLog,
    TimeProvider clock) : SaveChangesInterceptor
{
    public static readonly JsonSerializerOptions EventSerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly ConcurrentDictionary<Type, HashSet<string>> IgnoredAuditProperties = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is not ModuleDbContext moduleContext)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        var userId = currentUser.UserId;
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditEntry and not AuditEvent and not OutboxMessage and not NumberSequence)
            .ToList();

        var audits = new List<AuditEntry>();

        foreach (var entry in entries)
        {
            EnforceTenant(entry);
            var softDeleted = ApplySoftDelete(entry, now, userId);
            StampAuditable(entry, now, userId);

            var audit = BuildAuditEntry(moduleContext.Schema, entry, softDeleted, now, userId);
            if (audit is not null)
            {
                audits.Add(audit);
            }
        }

        context.Set<AuditEntry>().AddRange(audits);
        context.Set<AuditEvent>().AddRange(auditLog.Drain());
        WriteOutbox(context, now);
    }

    private void EnforceTenant(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned owned)
        {
            return;
        }

        if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
        {
            owned.AssignTenant(tenantContext.TenantId
                ?? throw new InvalidOperationException($"Cannot create {entry.Metadata.ClrType.Name} without a tenant context."));
            return;
        }

        if (tenantContext.TenantId is { } current && owned.TenantId != current)
        {
            throw new InvalidOperationException(
                $"Cross-tenant write blocked on {entry.Metadata.ClrType.Name} {entry.Property(nameof(Entity.Id)).CurrentValue}.");
        }
    }

    private static bool ApplySoftDelete(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry is not { State: EntityState.Deleted, Entity: ISoftDeletable })
        {
            return false;
        }

        entry.State = EntityState.Modified;
        entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
        entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
        entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = userId;
        return true;
    }

    private static void StampAuditable(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry.Entity is not IAuditable)
        {
            return;
        }

        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditable.CreatedBy)).CurrentValue = userId;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
            entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
            entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
            entry.Property(nameof(IAuditable.UpdatedBy)).CurrentValue = userId;
        }
    }

    private AuditEntry? BuildAuditEntry(string module, EntityEntry entry, bool softDeleted, DateTimeOffset now, Guid? userId)
    {
        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Created,
            EntityState.Deleted => AuditAction.Deleted,
            EntityState.Modified when softDeleted => AuditAction.SoftDeleted,
            EntityState.Modified => AuditAction.Updated,
            _ => (AuditAction?)null,
        };

        if (action is null)
        {
            return null;
        }

        var ignored = IgnoredAuditProperties.GetOrAdd(entry.Metadata.ClrType, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal));

        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var properties = entry.Properties.Concat(entry.ComplexProperties.SelectMany(c => c.Properties));

        foreach (var property in properties)
        {
            var name = property.Metadata.Name;
            if (property.Metadata.IsShadowProperty() || name is nameof(IAuditable.UpdatedAt) or nameof(IAuditable.UpdatedBy))
            {
                continue;
            }

            var redacted = ignored.Contains(name);
            switch (action)
            {
                case AuditAction.Created:
                    changes[name] = new { @new = redacted ? "***" : property.CurrentValue };
                    break;
                case AuditAction.Deleted:
                    changes[name] = new { old = redacted ? "***" : property.OriginalValue };
                    break;
                default:
                    if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                    {
                        changes[name] = redacted
                            ? new { old = (object?)"***", @new = (object?)"***" }
                            : new { old = property.OriginalValue, @new = property.CurrentValue };
                    }

                    break;
            }
        }

        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        var key = entry.Metadata.FindPrimaryKey()?.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString());

        return new AuditEntry
        {
            TenantId = (entry.Entity as ITenantOwned)?.TenantId ?? tenantContext.TenantId,
            UserId = userId,
            Module = module,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = key is null ? string.Empty : string.Join(",", key),
            Action = action.Value,
            Changes = JsonSerializer.Serialize(changes, EventSerializerOptions),
            IpAddress = requestInfo.IpAddress,
            UserAgent = requestInfo.UserAgent,
            CorrelationId = requestInfo.CorrelationId,
            OccurredAt = now,
        };
    }

    private void WriteOutbox(DbContext context, DateTimeOffset now)
    {
        var aggregates = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var type = domainEvent.GetType();
                context.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    TenantId = (domainEvent as IIntegrationEvent)?.TenantId ?? (aggregate as ITenantOwned)?.TenantId ?? tenantContext.TenantId,
                    Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
                    Content = JsonSerializer.Serialize(domainEvent, type, EventSerializerOptions),
                    OccurredAt = now,
                    CorrelationId = requestInfo.CorrelationId,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
