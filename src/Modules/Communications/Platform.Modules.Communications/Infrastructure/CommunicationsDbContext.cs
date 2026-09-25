using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Domain;

namespace Platform.Modules.Communications.Infrastructure;

public sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "comms";

    public override string Schema => SchemaName;

    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<PrayerRequest> PrayerRequests => Set<PrayerRequest>();
    public DbSet<MessageTemplate> Templates => Set<MessageTemplate>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<MessageDelivery> Deliveries => Set<MessageDelivery>();
    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();
    public DbSet<Notification> Notifications => Set<Notification>();
}

internal sealed class CommunicationsDbContextFactory : DesignTimeFactory<CommunicationsDbContext>, IDesignTimeDbContextFactory<CommunicationsDbContext>
{
    protected override string Schema => CommunicationsDbContext.SchemaName;

    protected override CommunicationsDbContext Create(DbContextOptions<CommunicationsDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
