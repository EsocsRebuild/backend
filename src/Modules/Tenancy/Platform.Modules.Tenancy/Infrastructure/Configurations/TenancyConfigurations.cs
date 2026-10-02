using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Tenancy.Domain;

namespace Platform.Modules.Tenancy.Infrastructure.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Property(x => x.Slug).HasMaxLength(63).IsCaseInsensitive();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.LegalName).HasMaxLength(200);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.PlanCode).HasMaxLength(64);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength();
        builder.Property(x => x.DefaultLocale).HasMaxLength(16);
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.ContactPhone).HasMaxLength(32);
        builder.Property(x => x.WebsiteUrl).HasMaxLength(512);
        builder.Property(x => x.LogoUrl).HasMaxLength(1024);
        builder.HasAddress(x => x.Address);

        builder.HasMany(x => x.Domains).WithOne().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Domains).AutoInclude(false);
    }
}

internal sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("tenant_domains");
        builder.Property(x => x.Host).HasMaxLength(253).IsCaseInsensitive();
        builder.HasIndex(x => x.Host).IsUnique();
        builder.Property(x => x.VerificationToken).HasMaxLength(64);
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units");
        builder.Property(x => x.Slug).HasMaxLength(120).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Kind).HasConversion(k => UnitKinds.Format(k), v => Parse(v)).HasMaxLength(32);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Path).HasMaxLength(2000);
        builder.HasIndex(x => new { x.TenantId, x.Path }).HasOperators("uuid_ops", "text_pattern_ops");
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Tagline).HasMaxLength(300);
        builder.Property(x => x.About).HasColumnType("text[]");
        builder.Property(x => x.Locality).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.Phones).HasColumnType("text[]");
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.OwnsOne(x => x.Cover, b => b.ToJson());
        builder.OwnsOne(x => x.Avatar, b => b.ToJson());
        builder.OwnsMany(x => x.Leaders, b => b.ToJson());
        builder.HasIndex(x => new { x.TenantId, x.ParentId, x.SortOrder });
        builder.HasIndex(x => new { x.TenantId, x.Kind });
        builder.HasOne<Unit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }

    private static UnitKind Parse(string value) =>
        UnitKinds.TryParse(value, out var kind) ? kind : throw new InvalidOperationException($"Unknown unit kind '{value}'.");
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        builder.ToTable("settings");
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.Value).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
    }
}
