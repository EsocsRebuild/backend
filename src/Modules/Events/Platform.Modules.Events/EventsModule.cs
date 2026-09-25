using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Events.Features;
using Platform.Modules.Events.Infrastructure;

namespace Platform.Modules.Events;

public sealed class EventsModule : IModule
{
    public string Name => "Events";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EventsDbContext>(configuration, EventsDbContext.SchemaName);
        services.AddScoped<OccurrenceSync>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<AttendanceService>();
        services.AddHostedService<OccurrenceScheduler>();
        services.AddHandlersAndValidators(typeof(EventsModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        EventEndpoints.Map(endpoints);
        AttendanceEndpoints.Map(endpoints);
    }
}
