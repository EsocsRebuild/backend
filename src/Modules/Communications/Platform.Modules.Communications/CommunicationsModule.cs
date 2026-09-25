using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Delivery;
using Platform.Modules.Communications.Features;
using Platform.Modules.Communications.Infrastructure;

namespace Platform.Modules.Communications;

public sealed class CommunicationsModule : IModule
{
    public string Name => "Communications";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CommunicationsDbContext>(configuration, CommunicationsDbContext.SchemaName);

        // Development senders; register real providers (Twilio/Termii, Firebase) before this module to override.
        services.TryAddScoped<ISmsSender, LoggingSmsSender>();
        services.TryAddScoped<IPushSender, LoggingPushSender>();
        services.AddHostedService<BroadcastDispatcher>();
        services.AddHandlersAndValidators(typeof(CommunicationsModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CommunicationsEndpoints.Map(endpoints);
}
