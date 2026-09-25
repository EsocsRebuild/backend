using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Events.Domain;

namespace Platform.Modules.Events.Infrastructure.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("events");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Slug).HasMaxLength(200).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Type).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Visibility).IsEnumText();
        builder.Property(x => x.Summary).HasMaxLength(500);
        builder.Property(x => x.Description).HasMaxLength(20_000);
        builder.Property(x => x.CoverImageUrl).HasMaxLength(1024);
        builder.Property(x => x.Location).HasMaxLength(300);
        builder.Property(x => x.OnlineUrl).HasMaxLength(1024);
        builder.Property(x => x.TimeZone).HasMaxLength(64);
        builder.Property(x => x.RecurrenceRule).HasMaxLength(200);
        builder.Ignore(x => x.Duration);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.StartsAt });
        builder.HasMany(x => x.Occurrences).WithOne().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OccurrenceConfiguration : IEntityTypeConfiguration<EventOccurrence>
{
    public void Configure(EntityTypeBuilder<EventOccurrence> builder)
    {
        builder.ToTable("occurrences");
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.CheckInCode).HasMaxLength(16);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.EventId, x.StartsAt }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.StartsAt });
        builder.HasIndex(x => x.CheckInCode);
    }
}

internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.ToTable("registrations");
        builder.Property(x => x.FullName).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.TicketCode).HasMaxLength(32);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Ignore(x => x.Seats);
        builder.HasIndex(x => x.TicketCode).IsUnique();
        builder.HasIndex(x => new { x.OccurrenceId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.PersonId });
        builder.HasIndex(x => new { x.TenantId, x.UserId });
        builder.HasOne<EventOccurrence>().WithMany().HasForeignKey(x => x.OccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("attendance_records");
        builder.Property(x => x.Method).IsEnumText();
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.OccurrenceId, x.PersonId }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(x => new { x.TenantId, x.PersonId, x.CheckedInAt });
        builder.HasOne<EventOccurrence>().WithMany().HasForeignKey(x => x.OccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HeadCountConfiguration : IEntityTypeConfiguration<HeadCount>
{
    public void Configure(EntityTypeBuilder<HeadCount> builder)
    {
        builder.ToTable("head_counts");
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => x.OccurrenceId).IsUnique();
        builder.HasOne<EventOccurrence>().WithMany().HasForeignKey(x => x.OccurrenceId).OnDelete(DeleteBehavior.Cascade);
    }
}
