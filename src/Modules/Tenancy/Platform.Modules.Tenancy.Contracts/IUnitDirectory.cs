namespace Platform.Modules.Tenancy.Contracts;

public sealed record UnitSummary(Guid Id, string Slug, string Name, string Kind, Guid? ParentId);

/// <summary>Read-only access to the organisation's unit hierarchy for other modules (cached).</summary>
public interface IUnitDirectory
{
    Task<IReadOnlyDictionary<Guid, UnitSummary>> GetAsync(IEnumerable<Guid> unitIds, CancellationToken cancellationToken);

    /// <summary>The unit and everything beneath it.</summary>
    Task<IReadOnlyList<Guid>> GetSubtreeIdsAsync(Guid unitId, CancellationToken cancellationToken);

    /// <summary>Units members can belong to ("parishes"), within an optional scope.</summary>
    Task<IReadOnlyList<UnitSummary>> GetParishesAsync(Guid? withinUnitId, CancellationToken cancellationToken);
}
