using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Features;
using Platform.Modules.Giving.Infrastructure;
using Platform.Modules.Giving.Payments;

namespace Platform.Modules.Giving;

public sealed class GivingModule : IModule
{
    public string Name => "Giving";

    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GivingDbContext>(configuration, GivingDbContext.SchemaName);
        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.SectionName));
        services.AddScoped<DonationRecorder>();

        // Payment gateways are keyed by provider name (matches the webhook route segment).
        services.AddKeyedScoped<IPaymentGateway, SandboxPaymentGateway>("Sandbox");
        services.AddHttpClient<PaystackPaymentGateway>(c => c.Timeout = TimeSpan.FromSeconds(20)).AddStandardResilienceHandler();
        services.AddKeyedScoped<IPaymentGateway>("Paystack", (sp, _) => sp.GetRequiredService<PaystackPaymentGateway>());

        services.AddHandlersAndValidators(typeof(GivingModule).Assembly);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DonationEndpoints.Map(endpoints);
        GivingManagementEndpoints.Map(endpoints);
        OnlineGivingEndpoints.Map(endpoints);
    }
}
