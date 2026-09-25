using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.People.Contracts;
using Platform.Modules.People.Features;
using Platform.Modules.People.Infrastructure;

namespace Platform.Modules.People;

public sealed class PeopleModule : IModule
{
    public string Name => "People";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PeopleDbContext>(configuration, PeopleDbContext.SchemaName);
        services.AddScoped<IPeopleDirectory, PeopleDirectory>();
        services.AddScoped<PersonFactory>();
        services.AddScoped<CustomFieldValidator>();
        services.AddHandlersAndValidators(typeof(PeopleModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PeopleEndpoints.Map(endpoints);
        CareEndpoints.Map(endpoints);
        MyProfileEndpoints.Map(endpoints);
    }
}
