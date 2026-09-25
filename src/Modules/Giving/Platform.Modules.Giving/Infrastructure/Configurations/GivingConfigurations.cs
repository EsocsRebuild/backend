using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Domain;

namespace Platform.Modules.Giving.Infrastructure.Configurations;

internal sealed class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("funds");
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Code).HasMaxLength(16).IsCaseInsensitive();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("is_deleted = false");
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.HasMoney(x => x.Goal);
        builder.HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PledgeConfiguration : IEntityTypeConfiguration<Pledge>
{
    public void Configure(EntityTypeBuilder<Pledge> builder)
    {
        builder.ToTable("pledges");
        builder.HasMoney(x => x.Amount);
        builder.Property(x => x.Frequency).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CampaignId, x.PersonId });
        builder.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<DonationBatch>
{
    public void Configure(EntityTypeBuilder<DonationBatch> builder)
    {
        builder.ToTable("batches");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Currency).HasColumnType("char(3)");
        builder.Property(x => x.ExpectedTotal).HasPrecision(18, 2);
        builder.Property(x => x.Status).IsEnumText();
        builder.HasIndex(x => new { x.TenantId, x.BatchDate });
    }
}

internal sealed class DonationConfiguration : IEntityTypeConfiguration<Donation>
{
    public void Configure(EntityTypeBuilder<Donation> builder)
    {
        builder.ToTable("donations");
        builder.Property(x => x.ReceiptNumber).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique();
        builder.Property(x => x.DonorName).HasMaxLength(200);
        builder.Property(x => x.DonorEmail).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.Method).IsEnumText();
        builder.Property(x => x.Channel).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Reference).HasMaxLength(100);
        builder.Property(x => x.Provider).HasMaxLength(32);
        builder.Property(x => x.ProviderReference).HasMaxLength(128);
        builder.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique().HasFilter("provider_reference IS NOT NULL");
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.StatusReason).HasMaxLength(500);
        builder.HasMoney(x => x.Total);

        builder.HasIndex(x => new { x.TenantId, x.ReceivedOn });
        builder.HasIndex(x => new { x.TenantId, x.PersonId, x.ReceivedOn });
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.CampaignId);

        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.DonationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DonationBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AllocationConfiguration : IEntityTypeConfiguration<DonationAllocation>
{
    public void Configure(EntityTypeBuilder<DonationAllocation> builder)
    {
        builder.ToTable("donation_allocations");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasIndex(x => x.FundId);
        builder.HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
    }
}
