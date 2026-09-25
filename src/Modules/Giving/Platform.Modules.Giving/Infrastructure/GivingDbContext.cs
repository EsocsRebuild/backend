using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Domain;

namespace Platform.Modules.Giving.Infrastructure;

public sealed class GivingDbContext(DbContextOptions<GivingDbContext> options, ITenantContext tenantContext) : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "giving";

    public override string Schema => SchemaName;

    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Pledge> Pledges => Set<Pledge>();
    public DbSet<DonationBatch> Batches => Set<DonationBatch>();
    public DbSet<Donation> Donations => Set<Donation>();
    public DbSet<DonationAllocation> Allocations => Set<DonationAllocation>();
}

internal sealed class GivingDbContextFactory : DesignTimeFactory<GivingDbContext>, IDesignTimeDbContextFactory<GivingDbContext>
{
    protected override string Schema => GivingDbContext.SchemaName;

    protected override GivingDbContext Create(DbContextOptions<GivingDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
