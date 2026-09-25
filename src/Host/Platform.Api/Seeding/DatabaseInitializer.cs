using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Modules;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Features;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Tenancy.Contracts;
using Platform.Modules.Tenancy.Features;
using Platform.Modules.Tenancy.Infrastructure;

namespace Platform.Api.Seeding;

internal sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string TenantSlug { get; set; } = "grace";
    public string TenantName { get; set; } = "Grace Community Church";
    public string TimeZone { get; set; } = "Africa/Lagos";
    public string Currency { get; set; } = "NGN";
    public string OwnerEmail { get; set; } = "admin@example.com";
    public string OwnerPassword { get; set; } = null!;
    public string OwnerFirstName { get; set; } = "Church";
    public string OwnerLastName { get; set; } = "Administrator";
    public bool OwnerIsPlatformAdmin { get; set; } = true;
}

/// <summary>
/// Applies module migrations (when enabled) and seeds the first organisation + owner in
/// development. Production deploys run migrations as a separate step (migration bundle).
/// </summary>
internal static partial class DatabaseInitializer
{
    public static async Task InitialiseAsync(WebApplication app)
    {
        var config = app.Configuration;
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        if (config.GetValue<bool>("Database:MigrateOnStartup"))
        {
            // Identity first: it owns the shared audit schema used by every module's writes.
            var databases = services.GetServices<IModuleDatabase>()
                .OrderBy(d => d.ContextType == typeof(IdentityDbContext) ? 0 : 1);
            foreach (var database in databases)
            {
                var context = (DbContext)services.GetRequiredService(database.ContextType);
                await context.Database.MigrateAsync();
                LogMigrated(logger, database.ContextType.Name);
            }
        }

        var seed = config.GetSection(SeedOptions.SectionName).Get<SeedOptions>();
        if (seed is { Enabled: true })
        {
            await SeedAsync(services, seed, logger);
        }
    }

    private static async Task SeedAsync(IServiceProvider services, SeedOptions seed, ILogger logger)
    {
        var tenancy = services.GetRequiredService<TenancyDbContext>();
        var tenant = await tenancy.Tenants.FirstOrDefaultAsync(t => t.Slug == seed.TenantSlug);
        if (tenant is null)
        {
            var result = await services.GetRequiredService<ICommandHandler<PlatformTenants.CreateCommand, TenantResponse>>().Handle(
                new PlatformTenants.CreateCommand(seed.TenantSlug, seed.TenantName, seed.OwnerEmail, seed.OwnerFirstName,
                    seed.OwnerLastName, "church", seed.TimeZone, seed.Currency, "en"), CancellationToken.None);
            if (result.IsFailure)
            {
                throw new InvalidOperationException($"Seeding failed: {result.Error.Description}");
            }

            // Provision identity synchronously so the owner can sign in immediately (the outbox
            // will deliver the same event again later; provisioning is idempotent).
            services.GetRequiredService<ITenantContextSetter>().SetTenant(result.Value.Id);
            await services.GetRequiredService<TenantProvisioning>().Handle(new TenantCreatedIntegrationEvent(
                result.Value.Id, seed.TenantSlug, seed.TenantName, seed.Currency, seed.OwnerEmail, seed.OwnerFirstName, seed.OwnerLastName), CancellationToken.None);
            LogSeededTenant(logger, seed.TenantSlug);
        }

        if (string.IsNullOrWhiteSpace(seed.OwnerPassword))
        {
            return;
        }

        var identity = services.GetRequiredService<IdentityDbContext>();
        var owner = await identity.Users.FirstOrDefaultAsync(u => u.Email == seed.OwnerEmail.ToLowerInvariant());
        if (owner is { PasswordHash: null })
        {
            owner.SetPassword(services.GetRequiredService<IPasswordHasher<User>>().HashPassword(owner, seed.OwnerPassword), DateTimeOffset.UtcNow);
            owner.ConfirmEmail();
            owner.GrantPlatformAdmin(seed.OwnerIsPlatformAdmin);
            var invited = await identity.Memberships.IgnoreQueryFilters().Where(m => m.UserId == owner.Id && m.Status == MembershipStatus.Invited).ToListAsync();
            invited.ForEach(m => m.Activate(DateTimeOffset.UtcNow));
            await identity.SaveChangesAsync();
            LogSeededOwner(logger, seed.OwnerEmail);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated {Context}")]
    private static partial void LogMigrated(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded organisation '{Slug}'")]
    private static partial void LogSeededTenant(ILogger logger, string slug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded owner account {Email}")]
    private static partial void LogSeededOwner(ILogger logger, string email);
}
