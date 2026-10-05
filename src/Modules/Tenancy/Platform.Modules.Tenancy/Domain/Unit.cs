using Platform.SharedKernel.Domain;

namespace Platform.Modules.Tenancy.Domain;

/// <summary>
/// Levels of the organisation, from the worldwide body down to a single house of prayer. Stored and
/// served as kebab-case (<c>holy-order</c>, <c>special-area</c>…) to match the website's content model.
/// </summary>
public enum UnitKind
{
    HolyOrder,
    Headquarters,
    Cmc,
    Province,
    SpecialArea,
    District,
    Branch,
    Section,
    Directorate,
}

public enum UnitStatus
{
    Active,
    Inactive,
}

public sealed record UnitLeader(string Name, string Role, string? PersonSlug);

/// <summary>
/// One organisational unit. Every level has the same shape, so any level gets the same website page,
/// feed and directory entry. <see cref="Path"/> is the materialised ancestry (<c>/rootId/…/thisId/</c>)
/// used for fast "everything under this unit" queries (parish-scoped admins, feeds that include sub-units).
/// </summary>
public sealed class Unit : TenantAggregateRoot
{
    private Unit() { }

    public string Slug { get; private set; } = null!;
    public UnitKind Kind { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid? ParentId { get; private set; }
    public string Path { get; private set; } = null!;
    public int Depth { get; private set; }
    public UnitStatus Status { get; private set; }

    /// <summary>Members can belong here (shown as a "parish" in the admin portal and sign-up form).</summary>
    public bool HoldsMembers { get; private set; }

    public string? Tagline { get; private set; }
    public List<string> About { get; private set; } = [];
    public string? Locality { get; private set; }
    public string? Address { get; private set; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string? Country { get; private set; }

    public DateOnly? Established { get; private set; }
    public ImageRef? Cover { get; private set; }
    public ImageRef? Avatar { get; private set; }
    public List<UnitLeader> Leaders { get; private set; } = [];
    public List<string> Phones { get; private set; } = [];
    public string? Email { get; private set; }
    public int SortOrder { get; private set; }

    public static Unit Create(Guid tenantId, string slug, UnitKind kind, string name, Unit? parent)
    {
        if (parent is not null && parent.TenantId != tenantId)
        {
            throw new DomainException("A unit's parent must belong to the same organisation.");
        }

        var unit = new Unit
        {
            Slug = slug,
            Kind = kind,
            Name = name.Trim(),
            Status = UnitStatus.Active,
            HoldsMembers = kind is UnitKind.Branch or UnitKind.Headquarters,
        };
        unit.AssignTenant(tenantId);
        unit.PlaceUnder(parent);
        return unit;
    }

    public void UpdateProfile(UnitProfile p)
    {
        Slug = p.Slug;
        Kind = p.Kind;
        Name = p.Name.Trim();
        Status = p.Status;
        HoldsMembers = p.HoldsMembers;
        Tagline = p.Tagline;
        About = p.About?.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList() ?? [];
        Locality = p.Locality;
        Address = p.Address;
        Country = p.Country?.ToUpperInvariant();
        Established = p.Established;
        Cover = p.Cover;
        Avatar = p.Avatar;
        Leaders = p.Leaders?.ToList() ?? [];
        Phones = p.Phones?.ToList() ?? [];
        Email = p.Email?.Trim().ToLowerInvariant();
        SortOrder = p.SortOrder;
    }

    /// <summary>Sets the parent. Callers must then re-path descendants (see <see cref="RebaseDescendant"/>).</summary>
    public void PlaceUnder(Unit? parent)
    {
        if (parent is not null && (parent.Id == Id || parent.Path.Contains($"/{Id:N}/", StringComparison.Ordinal)))
        {
            throw new DomainException("A unit cannot be placed under itself or one of its own sub-units.");
        }

        ParentId = parent?.Id;
        Path = $"{parent?.Path ?? "/"}{Id:N}/";
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <summary>Rewrites a descendant's ancestry after this unit moved from <paramref name="oldPath"/>.</summary>
    public void RebaseDescendant(Unit descendant, string oldPath)
    {
        if (!descendant.Path.StartsWith(oldPath, StringComparison.Ordinal))
        {
            throw new DomainException("Not a descendant.");
        }

        descendant.Path = Path + descendant.Path[oldPath.Length..];
        descendant.Depth = descendant.Path.Count(c => c == '/') - 2;
    }
}

public sealed record UnitProfile(
    string Slug, UnitKind Kind, string Name, UnitStatus Status, bool HoldsMembers, string? Tagline, IReadOnlyList<string>? About,
    string? Locality, string? Address, string? Country, DateOnly? Established, ImageRef? Cover, ImageRef? Avatar,
    IReadOnlyList<UnitLeader>? Leaders, IReadOnlyList<string>? Phones, string? Email, int SortOrder);

public static class UnitKinds
{
    private static readonly Dictionary<UnitKind, string> ToKebab = Enum.GetValues<UnitKind>()
        .ToDictionary(k => k, k => string.Concat(k.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString())));

    private static readonly Dictionary<string, UnitKind> FromKebab = ToKebab.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static string Format(UnitKind kind) => ToKebab[kind];

    public static bool TryParse(string? value, out UnitKind kind) => FromKebab.TryGetValue(value ?? string.Empty, out kind);

    public static IEnumerable<string> All => ToKebab.Values;
}
