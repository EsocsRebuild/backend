using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Groups.Domain;

namespace Platform.Modules.Groups.Infrastructure;

public sealed class GroupsDbContext(DbContextOptions<GroupsDbContext> options, ITenantContext tenantContext) : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "groups";

    public override string Schema => SchemaName;

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> Members => Set<GroupMember>();
}

internal sealed class GroupsDbContextFactory : DesignTimeFactory<GroupsDbContext>, IDesignTimeDbContextFactory<GroupsDbContext>
{
    protected override string Schema => GroupsDbContext.SchemaName;

    protected override GroupsDbContext Create(DbContextOptions<GroupsDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
