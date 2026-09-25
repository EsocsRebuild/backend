using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Content.Domain;

namespace Platform.Modules.Content.Infrastructure;

public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options, ITenantContext tenantContext) : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "content";

    public override string Schema => SchemaName;

    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<SermonSeries> Series => Set<SermonSeries>();
    public DbSet<Sermon> Sermons => Set<Sermon>();
    public DbSet<MediaAsset> Media => Set<MediaAsset>();
    public DbSet<Menu> Menus => Set<Menu>();
}

internal sealed class ContentDbContextFactory : DesignTimeFactory<ContentDbContext>, IDesignTimeDbContextFactory<ContentDbContext>
{
    protected override string Schema => ContentDbContext.SchemaName;

    protected override ContentDbContext Create(DbContextOptions<ContentDbContext> options, ITenantContext tenantContext) => new(options, tenantContext);
}
