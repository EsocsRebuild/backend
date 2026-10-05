using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Tenancy.Contracts;
using Platform.Modules.Tenancy.Features;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.Web.Endpoints;

namespace Platform.Modules.Tenancy;

public sealed class TenancyModule : IModule
{
    public string Name => "Tenancy";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TenancyDbContext>(configuration, TenancyDbContext.SchemaName);
        services.AddScoped<ITenantLookup, TenantLookup>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<UnitDirectory>();
        services.AddScoped<IUnitDirectory>(sp => sp.GetRequiredService<UnitDirectory>());
        services.AddHandlersAndValidators(typeof(TenancyModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var tenant = endpoints.MapModuleGroup("tenant", "Organisation");
        GetCurrentTenant.Map(tenant);
        UpdateCurrentTenant.Map(tenant);
        ManageTenantDomains.Map(tenant);

        Units.Map(endpoints);
        Settings.Map(endpoints);
        PlatformTenants.Map(endpoints);
        PublicTenant.Map(endpoints);
    }
}
