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
        services.AddHostedService<OutboxProcessorBackgroundService>();

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

        services.AddHttpClient<ResendEmailSender>();
        services.AddHttpClient<SendGridEmailSender>();
        services.AddHttpClient<PostmarkEmailSender>();

        var emailSection = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(emailSection);
        services.AddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailOptions>>();
            var provider = options.Value.Provider;

            IEmailSender coreSender = provider?.ToLowerInvariant() switch
            {
                "smtp" => new SmtpEmailSender(options, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SmtpEmailSender>>()),
                "resend" => sp.GetRequiredService<ResendEmailSender>(),
                "sendgrid" => sp.GetRequiredService<SendGridEmailSender>(),
                "postmark" => sp.GetRequiredService<PostmarkEmailSender>(),
                _ => new LoggingEmailSender(sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LoggingEmailSender>>())
            };

            return new ResilientEmailSender(coreSender, options, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientEmailSender>>());
        });

        return services;
    }
}
