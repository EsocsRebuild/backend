using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Application.Abstractions;
using Platform.Application.Messaging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Email;
using Platform.Infrastructure.Outbox;
using Platform.Infrastructure.Persistence.Interceptors;
using Platform.Infrastructure.Storage;
using Platform.Infrastructure.Tenancy;

namespace Platform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());

        services.TryAddScoped<IRequestInfo, NullRequestInfo>();
        services.AddScoped<PlatformSaveChangesInterceptor>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Platform.Application.Abstractions.IAuditLog>(sp => sp.GetRequiredService<AuditLog>());
        services.AddScoped<IEventDispatcher, InProcessEventDispatcher>();
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        // Caching: in-memory L1 + Redis L2 (when configured) behind HybridCache.
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "platform:";
            });
        }

        services.AddHybridCache(o =>
        {
            o.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromMinutes(10),
                LocalCacheExpiration = TimeSpan.FromMinutes(2),
            };
        });

        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        var emailSection = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(emailSection);
        if (string.Equals(emailSection["Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
