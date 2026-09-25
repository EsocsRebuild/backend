using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Groups.Features;
using Platform.Modules.Groups.Infrastructure;

namespace Platform.Modules.Groups;

public sealed class GroupsModule : IModule
{
    public string Name => "Groups";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GroupsDbContext>(configuration, GroupsDbContext.SchemaName);
        services.AddScoped<Platform.Modules.Groups.Contracts.IGroupDirectory, GroupDirectory>();
        services.AddHandlersAndValidators(typeof(GroupsModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => GroupEndpoints.Map(endpoints);
}
