using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Modules;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.Identity.Domain;
using Platform.Modules.Identity.Features;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.Tenancy.Contracts;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Features;
using Platform.Modules.Tenancy.Infrastructure;

namespace Platform.Api.Seeding;

internal sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string TenantSlug { get; set; } = "esocs";
    public string TenantName { get; set; } = "Eternal Sacred Order of Cherubim & Seraphim";
    public string TimeZone { get; set; } = "Africa/Lagos";
    public string Currency { get; set; } = "NGN";
    public string OwnerEmail { get; set; } = "admin@esocs.test";
    public string OwnerPassword { get; set; } = null!;
    public string OwnerFirstName { get; set; } = "Preview";
    public string OwnerLastName { get; set; } = "Administrator";
    public bool OwnerIsPlatformAdmin { get; set; } = true;

    /// <summary>Development-only sample parishes (created under the headquarters unit when none exist).</summary>
    public List<string> SampleParishes { get; set; } = [];
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
            var invitations = await identity.Invitations.IgnoreQueryFilters().Where(i => invited.Select(m => m.Id).Contains(i.MembershipId)).ToListAsync();
            invitations.ForEach(i => i.Accept(DateTimeOffset.UtcNow));
            await identity.SaveChangesAsync();
            LogSeededOwner(logger, seed.OwnerEmail);
        }

        await SeedParishesAsync(services, seed);
        await SeedCommunicationsAsync(services, seed);
    }

    private static async Task SeedCommunicationsAsync(IServiceProvider services, SeedOptions seed)
    {
        var comms = services.GetRequiredService<CommunicationsDbContext>();
        var tenancy = services.GetRequiredService<TenancyDbContext>();
        var tenantId = await tenancy.Tenants.Where(t => t.Slug == seed.TenantSlug).Select(t => t.Id).FirstAsync();
        services.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);

        if (await comms.Audiences.AnyAsync())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        // 1. Settings & Domain
        comms.SendingSettings.Add(SendingSettings.Create(seed.TenantName, "ESOCS Communications", "news@esocs.org", "53 Chatham Street, London SE17 1PA"));
        comms.SendingDomains.Add(SendingDomain.Create("esocs.org", "[]"));
        var domain = await comms.SendingDomains.FirstOrDefaultAsync(d => d.Domain == "esocs.org");
        domain?.Verify(now);

        // 2. Audiences
        var audNews = AudienceList.Create("Church newsletter", "Weekly updates for the whole congregation", true);
        var audYouth = AudienceList.Create("Youth fellowship", "Events and news for under-30s", true);
        comms.Audiences.AddRange(audNews, audYouth);
        await comms.SaveChangesAsync();

        // 3. Contacts
        for (int i = 1; i <= 25; i++)
        {
            comms.Contacts.Add(AudienceContact.Create(audNews.Id, $"member{i}@example.org", $"Member{i}", "Test", "subscribed", "import", null, now.AddDays(-i)));
        }
        for (int i = 1; i <= 10; i++)
        {
            comms.Contacts.Add(AudienceContact.Create(audYouth.Id, $"youth{i}@example.org", $"Youth{i}", "Test", "subscribed", "import", null, now.AddDays(-i)));
        }

        // 4. Template
        comms.EmailTemplates.Add(EmailTemplate.Create("Monthly newsletter", "Standard church newsletter template", "{}", null, "Preview Administrator"));

        // 5. Campaigns
        var statsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            sent = 36,
            delivered = 32,
            bounces = 4,
            opens = 25,
            uniqueOpens = 17,
            clicks = 5,
            uniqueClicks = 4,
            unsubscribes = 2,
            complaints = 0
        });

        var cmp1 = EmailCampaign.Create("Harvest Thanksgiving invitation", "You’re invited: Harvest Thanksgiving this Sunday", "{}", $"{{\"listIds\":[\"{audNews.Id}\"]}}", null, "Preview Administrator");
        cmp1.MarkSent(36, statsJson, now.AddDays(-2));

        var cmp2 = EmailCampaign.Create("Youth camp reminder", "Youth camp registration closes soon", "{}", $"{{\"listIds\":[\"{audYouth.Id}\"]}}", null, "Preview Administrator");

        comms.Campaigns.AddRange(cmp1, cmp2);

        // 6. Form
        var campFields = System.Text.Json.JsonSerializer.Serialize(new object[]
        {
            new { id = "fullname", type = "short_text", label = "Full name", required = true },
            new { id = "emailaddr", type = "email", label = "Email address", description = "We’ll send your confirmation here.", required = true },
            new { id = "session", type = "radio", label = "Which session will you attend?", required = true, options = new object[]
                {
                    new { id = "morning", label = "Morning (9am)" },
                    new { id = "evening", label = "Evening (5pm)" }
                }
            },
            new { id = "consent", type = "consent", label = "I’m happy to receive emails from the church", required = false }
        });

        var formSettings = System.Text.Json.JsonSerializer.Serialize(new
        {
            submitLabel = "Register",
            confirmationTitle = "You’re registered!",
            confirmationMessage = "We’ll email you the details soon.",
            redirectUrl = (string?)null,
            closesAt = (string?)null,
            responseLimit = 120,
            notifyEmails = new string[] { seed.OwnerEmail },
            audienceId = audYouth.Id.ToString()
        });

        var form = FormDefinition.Create("Youth camp registration", "youth-camp", "Register for our annual youth camp. Places are limited.", campFields, formSettings);
        form.Publish(now.AddDays(-5));
        comms.Forms.Add(form);

        await comms.SaveChangesAsync();
    }

    private static async Task SeedParishesAsync(IServiceProvider services, SeedOptions seed)
    {
        if (seed.SampleParishes.Count == 0)
        {
            return;
        }

        var tenancy = services.GetRequiredService<TenancyDbContext>();
        var tenantId = await tenancy.Tenants.Where(t => t.Slug == seed.TenantSlug).Select(t => t.Id).FirstAsync();
        services.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
        if (await tenancy.Units.AnyAsync(u => u.Kind == UnitKind.Branch))
        {
            return;
        }

        var headquarters = await tenancy.Units.FirstAsync(u => u.ParentId == null);
        foreach (var name in seed.SampleParishes)
        {
            tenancy.Units.Add(Unit.Create(tenantId, Platform.SharedKernel.Domain.Slug.From(name), UnitKind.Branch, name, headquarters));
        }

        await tenancy.SaveChangesAsync();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated {Context}")]
    private static partial void LogMigrated(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded organisation '{Slug}'")]
    private static partial void LogSeededTenant(ILogger logger, string slug);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded owner account {Email}")]
    private static partial void LogSeededOwner(ILogger logger, string email);
}
