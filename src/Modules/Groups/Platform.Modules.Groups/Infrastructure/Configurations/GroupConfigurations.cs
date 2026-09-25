using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Groups.Domain;

namespace Platform.Modules.Groups.Infrastructure.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Slug).HasMaxLength(200).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Type).IsEnumText();
        builder.Property(x => x.Visibility).IsEnumText();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024);
        builder.Property(x => x.MeetingSchedule).HasMaxLength(200);
        builder.Property(x => x.MeetingLocation).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Type });
        builder.HasIndex(x => x.ParentGroupId);
        builder.HasOne<Group>().WithMany().HasForeignKey(x => x.ParentGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");
        builder.Property(x => x.Role).IsEnumText();
        builder.Property(x => x.Status).IsEnumText();
        builder.HasIndex(x => new { x.GroupId, x.PersonId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PersonId });
    }
}
