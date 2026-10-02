using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure;

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "tenancy";

    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<TenantSetting> Settings => Set<TenantSetting>();
}

internal sealed class TenancyDbContextFactory : DesignTimeFactory<TenancyDbContext>, IDesignTimeDbContextFactory<TenancyDbContext>
{
    protected override string Schema => TenancyDbContext.SchemaName;

    protected override TenancyDbContext Create(DbContextOptions<TenancyDbContext> options, ITenantContext tenantContext) =>
        new(options, tenantContext);
}
