using System.Reflection;

namespace Platform.Application.Security;

/// <summary>
/// The permission catalogue: the single source of truth for authorisation, shared with the admin portal
/// (<c>src/lib/permissions.ts</c> + <c>permission-catalog.ts</c> must list the same strings).
/// Format: <c>area:verb</c>. Roles are named sets of these strings, editable per organisation.
/// </summary>
public static class Permissions
{
    public static class Dashboard
    {
        public const string View = "dashboard:view";
    }

    public static class Members
    {
        public const string View = "members:view";
        public const string Manage = "members:manage";
        public const string Export = "members:export";
    }

    public static class Pastoral
    {
        public const string View = "pastoral:view";
        public const string Manage = "pastoral:manage";
    }

    public static class Campaigns
    {
        public const string View = "campaigns:view";
        public const string Manage = "campaigns:manage";
        public const string Send = "campaigns:send";
    }

    public static class Audiences
    {
        public const string View = "audiences:view";
        public const string Manage = "audiences:manage";
    }

    public static class Templates
    {
        public const string Manage = "templates:manage";
    }

    public static class Forms
    {
        public const string View = "forms:view";
        public const string Manage = "forms:manage";
    }

    public static class Content
    {
        public const string View = "content:view";
        public const string Manage = "content:manage";
        public const string Publish = "content:publish";
    }

    public static class Community
    {
        public const string Moderate = "community:moderate";
    }

    public static class Prayer
    {
        public const string View = "prayer:view";
        public const string Manage = "prayer:manage";
    }

    public static class Events
    {
        public const string View = "events:view";
        public const string Manage = "events:manage";
        public const string RecordAttendance = "attendance:record";
    }

    public static class Groups
    {
        public const string View = "groups:view";
        public const string Manage = "groups:manage";
    }

    public static class Giving
    {
        public const string View = "giving:view";
        public const string Manage = "giving:manage";
        public const string Reports = "giving:reports";
    }

    public static class Store
    {
        public const string View = "store:view";
        public const string Manage = "store:manage";
    }

    public static class Users
    {
        public const string View = "users:view";
        public const string Manage = "users:manage";
    }

    public static class Roles
    {
        public const string Manage = "roles:manage";
    }

    public static class Audit
    {
        public const string View = "audit:view";
    }

    public static class Organisation
    {
        public const string Manage = "organisation:manage";
    }

    public static class Settings
    {
        public const string Manage = "settings:manage";
    }

    private static readonly Lazy<IReadOnlyList<string>> _ordered = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList());

    private static readonly Lazy<IReadOnlySet<string>> _all = new(() => _ordered.Value.ToHashSet(StringComparer.Ordinal));

    public static IReadOnlySet<string> All => _all.Value;

    /// <summary>Catalogue order (as declared), for stable API output.</summary>
    public static IReadOnlyList<string> Ordered => _ordered.Value;

    public static bool IsKnown(string permission) => All.Contains(permission);
}
