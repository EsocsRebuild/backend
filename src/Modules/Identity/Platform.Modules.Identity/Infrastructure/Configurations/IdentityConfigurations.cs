using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Domain;

namespace Platform.Modules.Identity.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(x => x.Email).HasMaxLength(256).IsCaseInsensitive();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Phone).HasMaxLength(32);
        builder.Property(x => x.AvatarUrl).HasMaxLength(1024);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.SecurityStamp).HasMaxLength(64);
        builder.Property(x => x.TwoFactorSecret).HasMaxLength(1024);
        builder.Property(x => x.RecoveryCodeHashes).HasColumnType("text[]");
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<TenantMembership>
{
    public void Configure(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("memberships");
        builder.Property(x => x.Kind).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.Kind }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.PersonId });
        builder.OwnsOne(x => x.NotificationPreferences, b => b.ToJson());
        builder.Ignore(x => x.RoleId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.MembershipId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
{
    public void Configure(EntityTypeBuilder<MembershipRole> builder)
    {
        builder.ToTable("membership_roles");
        builder.HasKey(x => new { x.MembershipId, x.RoleId });
        builder.HasIndex(x => x.RoleId);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.Property(x => x.Name).HasMaxLength(60).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Description).HasMaxLength(280);
        builder.Property(x => x.SystemKey).HasMaxLength(60);
        builder.Property(x => x.Icon).HasMaxLength(16);
        builder.Property(x => x.Permissions).HasColumnType("text[]");
        builder.Ignore(x => x.IsSystem);
    }
}

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("sessions");
        builder.Property(x => x.ClientType).IsEnumText();
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.Property(x => x.PreviousTokenHash).HasMaxLength(64);
        builder.Property(x => x.SudoTokenHash).HasMaxLength(64);
        builder.Property(x => x.RevokedReason).HasMaxLength(64);
        builder.HasIndex(x => new { x.UserId, x.RevokedAt });
        builder.HasIndex(x => x.MembershipId);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(EntityTypeBuilder<StaffInvitation> builder)
    {
        builder.ToTable("invitations");
        builder.Property(x => x.Email).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.Property(x => x.InvitedByName).HasMaxLength(120);
        builder.HasIndex(x => x.MembershipId);
        builder.HasOne<TenantMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    public void Configure(EntityTypeBuilder<AccessRequest> builder)
    {
        builder.ToTable("access_requests");
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Email).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.Phone).HasMaxLength(32);
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.Email });
    }
}

internal sealed class OneTimeCodeConfiguration : IEntityTypeConfiguration<OneTimeCode>
{
    public void Configure(EntityTypeBuilder<OneTimeCode> builder)
    {
        builder.ToTable("one_time_codes");
        builder.Property(x => x.Purpose).IsEnumText();
        builder.Property(x => x.Destination).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.CodeHash).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.Purpose, x.Destination, x.SentAt });
        builder.HasIndex(x => x.ExpiresAt);
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Prefix).HasMaxLength(16);
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.Property(x => x.KeyHash).HasMaxLength(64);
        builder.Property(x => x.Scopes).HasColumnType("text[]");
    }
}
