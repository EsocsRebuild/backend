using Platform.SharedKernel.Domain;

namespace Platform.Modules.Identity.Domain;

/// <summary>A named, organisation-specific set of permissions from the catalogue.</summary>
public sealed class Role : TenantAggregateRoot
{
    private Role() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>Stable key of a built-in role (e.g. "Owner"); null for custom roles.</summary>
    public string? SystemKey { get; private set; }

    /// <summary>Built-in roles can't be deleted.</summary>
    public bool IsSystem => SystemKey is not null;

    /// <summary>The Owner role can't be edited.</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Offered on the admin sign-up page.</summary>
    public bool IsRequestable { get; private set; }

    /// <summary>Icon key for the sign-up page: church, finance, editor, viewer, admin.</summary>
    public string Icon { get; private set; } = "viewer";

    public List<string> Permissions { get; private set; } = [];

    public static Role Create(string name, string? description, IEnumerable<string> permissions, string? systemKey = null,
        bool locked = false, bool requestable = false, string icon = "viewer")
    {
        var role = new Role { Name = name, Description = description, SystemKey = systemKey, IsLocked = locked, IsRequestable = requestable, Icon = icon };
        role.SetPermissions(permissions);
        return role;
    }

    public void Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsLocked)
        {
            throw new DomainException("This role can’t be changed.");
        }

        Name = name.Trim();
        Description = description;
        SetPermissions(permissions);
    }

    private void SetPermissions(IEnumerable<string> permissions)
    {
        var ordered = Application.Security.Permissions.Ordered;
        var requested = permissions.ToHashSet(StringComparer.Ordinal);
        var unknown = requested.Where(p => !Application.Security.Permissions.IsKnown(p)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException($"Unknown permissions: {string.Join(", ", unknown)}");
        }

        Permissions = ordered.Where(requested.Contains).ToList();
    }
}
