using Platform.Infrastructure.Modules;
using Platform.Modules.Communications;
using Platform.Modules.Content;
using Platform.Modules.Events;
using Platform.Modules.Giving;
using Platform.Modules.Groups;
using Platform.Modules.Identity;
using Platform.Modules.People;
using Platform.Modules.Tenancy;

namespace Platform.Api;

/// <summary>The composition root's module list. Adding a module = one line here.</summary>
internal static class Modules
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new TenancyModule(),
        new IdentityModule(),
        new PeopleModule(),
        new GroupsModule(),
        new EventsModule(),
        new GivingModule(),
        new ContentModule(),
        new CommunicationsModule(),
    ];
}
