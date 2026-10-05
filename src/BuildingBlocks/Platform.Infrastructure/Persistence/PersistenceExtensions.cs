using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Outbox;
using Platform.Infrastructure.Persistence.Interceptors;

namespace Platform.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public const string ConnectionStringName = "Database";

    /// <summary>
    /// Registers a module DbContext against the shared PostgreSQL database with its own schema
    /// and migrations history table, plus its outbox processor.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : ModuleDbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<TContext>((sp, options) =>
        {
            ConfigureNpgsql(options, connectionString, schema, typeof(TContext).Assembly.GetName().Name!);
            options.AddInterceptors(sp.GetRequiredService<PlatformSaveChangesInterceptor>());
        });

        services.AddSingleton<IModuleDatabase>(new ModuleDatabase(typeof(TContext)));
        return services;
    }

    public static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString, string schema, string migrationsAssembly)
    {
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
                npgsql.MigrationsAssembly(migrationsAssembly);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention();
    }
}
