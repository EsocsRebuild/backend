using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Content.Features;
using Platform.Modules.Content.Infrastructure;

namespace Platform.Modules.Content;

public sealed class ContentModule : IModule
{
    public string Name => "Content";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContentDbContext>(configuration, ContentDbContext.SchemaName);
        services.AddHostedService<ScheduledPublisher>();
        services.AddHandlersAndValidators(typeof(ContentModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ContentAdminEndpoints.Map(endpoints);
        PublicContentEndpoints.Map(endpoints);
    }
}
