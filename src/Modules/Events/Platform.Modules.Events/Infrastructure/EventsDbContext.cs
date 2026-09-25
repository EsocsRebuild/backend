using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Events.Domain;

namespace Platform.Modules.Events.Infrastructure;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options, ITenantContext tenantContext) : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "events";

    public override string Schema => SchemaName;

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventOccurrence> Occurrences => Set<EventOccurrence>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<HeadCount> HeadCounts => Set<HeadCount>();
}

internal sealed class EventsDbContextFactory : DesignTimeFactory<EventsDbContext>, IDesignTimeDbContextFactory<EventsDbContext>
{
    protected override string Schema => EventsDbContext.SchemaName;

    protected override EventsDbContext Create(DbContextOptions<EventsDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
