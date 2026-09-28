using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Caching;
using Platform.Modules.Tenancy.Contracts;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

/// <summary>
/// Cached, tenant-wide snapshot of the unit tree (organisations have hundreds to a few thousand units),
/// invalidated whenever a unit changes (<see cref="CacheKeys.TenantTag"/>).
/// </summary>
internal sealed class UnitDirectory(TenancyDbContext db, ITenantContext tenant, HybridCache cache) : IUnitDirectory
{
    internal sealed record Node(Guid Id, string Slug, string Name, string Kind, Guid? ParentId, string Path, bool HoldsMembers, bool Active, int SortOrder);

    public async Task<IReadOnlyDictionary<Guid, UnitSummary>> GetAsync(IEnumerable<Guid> unitIds, CancellationToken cancellationToken)
    {
        var tree = await TreeAsync(cancellationToken);
        return unitIds.Distinct().Where(tree.ContainsKey)
            .ToDictionary(id => id, id => ToSummary(tree[id]));
    }

    public async Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var tree = await TreeAsync(cancellationToken);
        if (!tree.TryGetValue(unitId, out var root))
        {
            return [];
        }

        return tree.Values.Where(n => n.Path.StartsWith(root.Path, StringComparison.Ordinal)).Select(n => n.Id).ToList();
    }

    public async Task<IReadOnlyList<UnitSummary>> GetParishesAsync(Guid? withinUnitId, CancellationToken cancellationToken)
    {
        var tree = await TreeAsync(cancellationToken);
        var nodes = tree.Values.Where(n => n.HoldsMembers && n.Active);
        if (withinUnitId is { } scope && tree.TryGetValue(scope, out var root))
        {
            nodes = nodes.Where(n => n.Path.StartsWith(root.Path, StringComparison.Ordinal));
        }

        return nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(ToSummary).ToList();
    }

    internal async Task<IReadOnlyDictionary<Guid, Node>> TreeAsync(CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var nodes = await cache.GetOrCreateAsync($"units:{tenantId:N}", async token =>
            await db.Units.AsNoTracking()
                .Select(u => new Node(u.Id, u.Slug, u.Name, UnitKinds.Format(u.Kind), u.ParentId, u.Path, u.HoldsMembers,
                    u.Status == UnitStatus.Active, u.SortOrder))
                .ToListAsync(token),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10) },
            tags: [CacheKeys.TenantTag(tenantId)],
            cancellationToken: ct);
        return nodes.ToDictionary(n => n.Id);
    }

    private static UnitSummary ToSummary(Node n) => new(n.Id, n.Slug, n.Name, n.Kind, n.ParentId);
}
