using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.People.Domain;

namespace Platform.Modules.People.Infrastructure;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "people";

    public override string Schema => SchemaName;

    public DbSet<Person> People => Set<Person>();
    public DbSet<MembershipStatusChange> StatusChanges => Set<MembershipStatusChange>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<PersonNote> Notes => Set<PersonNote>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<CustomFieldDefinition> CustomFields => Set<CustomFieldDefinition>();
}

internal sealed class PeopleDbContextFactory : DesignTimeFactory<PeopleDbContext>, IDesignTimeDbContextFactory<PeopleDbContext>
{
    protected override string Schema => PeopleDbContext.SchemaName;

    protected override PeopleDbContext Create(DbContextOptions<PeopleDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
