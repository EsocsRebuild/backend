using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.People.Domain;

namespace Platform.Modules.People.Infrastructure.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("people");
        builder.Property(x => x.MemberNumber).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.MemberNumber }).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(32);
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.MiddleName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.PreferredName).HasMaxLength(100);
        builder.Property(x => x.Gender).IsEnumText();
        builder.Property(x => x.MaritalStatus).IsEnumText();
        builder.Property(x => x.MembershipStatus).IsEnumText();
        builder.Property(x => x.HouseholdRole).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Email).HasMaxLength(256).IsCaseInsensitive();
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.Property(x => x.AlternatePhoneNumber).HasMaxLength(32);
        builder.Property(x => x.Occupation).HasMaxLength(100);
        builder.Property(x => x.Employer).HasMaxLength(200);
        builder.Property(x => x.PhotoUrl).HasMaxLength(1024);
        builder.Property(x => x.Source).HasMaxLength(100);
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.Property(x => x.CustomFields).HasColumnType("jsonb");
        builder.HasAddress(x => x.Address);
        builder.Ignore(x => x.FullName);

        builder.HasIndex(x => new { x.TenantId, x.LastName, x.FirstName });
        builder.HasIndex(x => new { x.TenantId, x.Email });
        builder.HasIndex(x => new { x.TenantId, x.PhoneNumber });
        builder.HasIndex(x => new { x.TenantId, x.MembershipStatus });
        builder.HasIndex(x => new { x.TenantId, x.UserId });
        builder.HasIndex(x => x.HouseholdId);
        builder.HasIndex(x => x.Tags).HasMethod("gin");

        builder.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Household>().WithMany().HasForeignKey(x => x.HouseholdId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class MembershipStatusChangeConfiguration : IEntityTypeConfiguration<MembershipStatusChange>
{
    public void Configure(EntityTypeBuilder<MembershipStatusChange> builder)
    {
        builder.ToTable("membership_status_changes");
        builder.Property(x => x.From).IsEnumText();
        builder.Property(x => x.To).IsEnumText();
        builder.Property(x => x.Reason).HasMaxLength(500);
    }
}

internal sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("households");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.PhoneNumber).HasMaxLength(32);
        builder.HasAddress(x => x.Address);
    }
}

internal sealed class PersonNoteConfiguration : IEntityTypeConfiguration<PersonNote>
{
    public void Configure(EntityTypeBuilder<PersonNote> builder)
    {
        builder.ToTable("person_notes");
        builder.Property(x => x.Category).IsEnumText();
        builder.Property(x => x.Visibility).IsEnumText();
        builder.Property(x => x.Body).HasMaxLength(10_000);
        builder.HasIndex(x => x.PersonId);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> builder)
    {
        builder.ToTable("follow_ups");
        builder.Property(x => x.Type).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.Property(x => x.Priority).IsEnumText();
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.Outcome).HasMaxLength(4000);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
        builder.HasIndex(x => new { x.AssignedToUserId, x.Status });
        builder.HasIndex(x => x.PersonId);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("custom_field_definitions");
        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Label).HasMaxLength(100);
        builder.Property(x => x.FieldType).IsEnumText();
        builder.Property(x => x.Options).HasColumnType("text[]");
        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique().HasFilter("is_deleted = false");
    }
}
