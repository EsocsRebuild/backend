using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Auditing;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    /// <summary>Identity owns the shared audit tables (security module).</summary>
    protected override bool OwnsAuditTable => true;

    public DbSet<User> Users => Set<User>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<StaffInvitation> Invitations => Set<StaffInvitation>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<OneTimeCode> Codes => Set<OneTimeCode>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
}

internal sealed class IdentityDbContextFactory : DesignTimeFactory<IdentityDbContext>, IDesignTimeDbContextFactory<IdentityDbContext>
{
    protected override string Schema => IdentityDbContext.SchemaName;

    protected override IdentityDbContext Create(DbContextOptions<IdentityDbContext> options, ITenantContext tenantContext) =>
        new(options, tenantContext);
}
