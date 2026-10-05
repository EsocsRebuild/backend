namespace Platform.Application.Security;

public sealed record SystemRoleDefinition(
    string Key, string Name, string Description, IReadOnlySet<string> Permissions, bool Locked, bool Requestable, string Icon);

/// <summary>
/// Roles provisioned for every new organisation. <see cref="Owner"/> holds everything and is locked;
/// the others may be edited per organisation. <c>Requestable</c> roles are offered on the sign-up page.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string ParishAdministrator = "Parish administrator";
    public const string Communications = "Communications";
    public const string ContentEditor = "Content editor";
    public const string Finance = "Finance";
    public const string Viewer = "Viewer";

    /// <summary>Default role for website / app member accounts (no portal access).</summary>
    public const string Member = "Member";

    public static IReadOnlyList<SystemRoleDefinition> Defaults { get; } =
    [
        new(Owner, Owner, "Full access. Can’t be changed.", Permissions.All, Locked: true, Requestable: false, "admin"),
        new(Administrator, Administrator, "Runs day-to-day administration.",
            Permissions.All.Where(p => p != Permissions.Roles.Manage).ToHashSet(), false, false, "admin"),
        new(ParishAdministrator, ParishAdministrator, "Manages members, events and giving for a parish.", Set(
            Permissions.Dashboard.View, Permissions.Members.View, Permissions.Members.Manage, Permissions.Pastoral.View,
            Permissions.Pastoral.Manage, Permissions.Events.View, Permissions.Events.Manage, Permissions.Events.RecordAttendance,
            Permissions.Groups.View, Permissions.Groups.Manage, Permissions.Giving.View, Permissions.Giving.Manage,
            Permissions.Prayer.View, Permissions.Forms.View), false, true, "church"),
        new(Communications, Communications, "Prepares and sends emails, builds forms.", Set(
            Permissions.Dashboard.View, Permissions.Campaigns.View, Permissions.Campaigns.Manage, Permissions.Campaigns.Send,
            Permissions.Audiences.View, Permissions.Audiences.Manage, Permissions.Templates.Manage, Permissions.Forms.View,
            Permissions.Forms.Manage), false, true, "editor"),
        new(ContentEditor, ContentEditor, "Publishes news, events and media on the website.", Set(
            Permissions.Dashboard.View, Permissions.Content.View, Permissions.Content.Manage, Permissions.Content.Publish,
            Permissions.Events.View, Permissions.Events.Manage, Permissions.Community.Moderate), false, true, "editor"),
        new(Finance, Finance, "Records giving and manages the store.", Set(
            Permissions.Dashboard.View, Permissions.Members.View, Permissions.Giving.View, Permissions.Giving.Manage,
            Permissions.Giving.Reports, Permissions.Store.View, Permissions.Store.Manage), false, true, "finance"),
        new(Viewer, Viewer, "Can look but not change anything.", Set(
            Permissions.Dashboard.View, Permissions.Members.View, Permissions.Campaigns.View, Permissions.Audiences.View,
            Permissions.Forms.View, Permissions.Content.View, Permissions.Events.View), false, true, "viewer"),
        new(Member, Member, "Website and app account. No access to the admin portal.", Set(), false, false, "viewer"),
    ];

    private static IReadOnlySet<string> Set(params string[] permissions) => permissions.ToHashSet(StringComparer.Ordinal);
}
