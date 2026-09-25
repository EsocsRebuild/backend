using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.Infrastructure.Persistence;

namespace Platform.Api.Configuration;

internal static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "clients";

    public static IServiceCollection AddApiPlatform(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info.Title = "Platform API";
            doc.Info.Version = "v1";
            doc.Info.Description = "Admin console, public website and mobile app API. Authenticate with a Bearer token or X-Api-Key; anonymous public endpoints need the X-Tenant header.";
            return Task.CompletedTask;
        }));

        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
            .WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("X-Request-Id")));

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
            o.AddPolicy("auth", ctx => RateLimitPartition.GetSlidingWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new SlidingWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6 }));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1) }));
        });

        var health = services.AddHealthChecks()
            .AddNpgSql(configuration.GetConnectionString(PersistenceExtensions.ConnectionStringName)!, name: "postgres", tags: ["ready"]);
        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            health.AddRedis(redis, name: "redis", tags: ["ready"]);
        }

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("platform-api", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();
                if (!string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                {
                    t.AddOtlpExporter();
                }
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentationIfAvailable();
                if (!string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                {
                    m.AddOtlpExporter();
                }
            });

        return services;
    }

    private static MeterProviderBuilder AddRuntimeInstrumentationIfAvailable(this MeterProviderBuilder builder) =>
        builder.AddMeter("System.Runtime");
}
