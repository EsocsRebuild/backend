using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Domain;

namespace Platform.Modules.Communications.Infrastructure.Configurations;

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Body).HasMaxLength(10_000);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024);
        builder.Property(x => x.LinkUrl).HasMaxLength(1024);
        builder.Property(x => x.Audience).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.HasIndex(x => new { x.TenantId, x.Status, x.PublishAt });
    }
}

internal sealed class PrayerRequestConfiguration : IEntityTypeConfiguration<PrayerRequest>
{
    public void Configure(EntityTypeBuilder<PrayerRequest> builder)
    {
        builder.ToTable("prayer_requests");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.Request).HasMaxLength(4000);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.AnswerNote).HasMaxLength(2000);
        builder.Ignore(x => x.DisplayName);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.UserId });
    }
}

internal sealed class TemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Channel).IsEnumText();
        builder.Property(x => x.Subject).HasMaxLength(200);
        builder.Property(x => x.Body).HasMaxLength(50_000);
    }
}

internal sealed class BroadcastConfiguration : IEntityTypeConfiguration<Broadcast>
{
    public void Configure(EntityTypeBuilder<Broadcast> builder)
    {
        builder.ToTable("broadcasts");
        builder.Property(x => x.Channel).IsEnumText();
        builder.Property(x => x.Subject).HasMaxLength(200);
        builder.Property(x => x.Body).HasMaxLength(50_000);
        builder.Property(x => x.Audience).HasColumnType("jsonb");
        builder.Property(x => x.Status).IsEnumText();
        builder.HasIndex(x => new { x.Status, x.ScheduledFor });
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<MessageDelivery>
{
    public void Configure(EntityTypeBuilder<MessageDelivery> builder)
    {
        builder.ToTable("message_deliveries");
        builder.Property(x => x.RecipientName).HasMaxLength(200);
        builder.Property(x => x.Destination).HasMaxLength(256);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Error).HasMaxLength(500);
        builder.HasIndex(x => new { x.BroadcastId, x.Status });
        builder.HasIndex(x => new { x.BroadcastId, x.PersonId }).IsUnique();
        builder.HasOne<Broadcast>().WithMany().HasForeignKey(x => x.BroadcastId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> builder)
    {
        builder.ToTable("devices");
        builder.Property(x => x.Platform).IsEnumText();
        builder.Property(x => x.Token).HasMaxLength(512);
        builder.Property(x => x.AppVersion).HasMaxLength(32);
        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId });
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(x => x.Category).HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Body).HasMaxLength(2000);
        builder.Property(x => x.Link).HasMaxLength(1024);
        builder.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
    }
}
