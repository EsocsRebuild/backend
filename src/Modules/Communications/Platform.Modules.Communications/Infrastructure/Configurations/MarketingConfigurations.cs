using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Domain;

namespace Platform.Modules.Communications.Infrastructure.Configurations;

internal sealed class AudienceListConfiguration : IEntityTypeConfiguration<AudienceList>
{
    public void Configure(EntityTypeBuilder<AudienceList> builder)
    {
        builder.ToTable("audience_lists");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}

internal sealed class AudienceContactConfiguration : IEntityTypeConfiguration<AudienceContact>
{
    public void Configure(EntityTypeBuilder<AudienceContact> builder)
    {
        builder.ToTable("audience_contacts");
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.FirstName).HasMaxLength(100);
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.Source).HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.ListId, x.Email }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ListId, x.Status });
    }
}

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ContentJson).HasColumnType("jsonb");
        builder.Property(x => x.UpdatedByName).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}

internal sealed class EmailCampaignConfiguration : IEntityTypeConfiguration<EmailCampaign>
{
    public void Configure(EntityTypeBuilder<EmailCampaign> builder)
    {
        builder.ToTable("email_campaigns");
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Subject).HasMaxLength(300);
        builder.Property(x => x.PreviewText).HasMaxLength(300);
        builder.Property(x => x.FromName).HasMaxLength(200);
        builder.Property(x => x.FromEmail).HasMaxLength(256);
        builder.Property(x => x.ReplyTo).HasMaxLength(256);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.AudienceJson).HasColumnType("jsonb");
        builder.Property(x => x.ContentJson).HasColumnType("jsonb");
        builder.Property(x => x.StatsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedByName).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

internal sealed class SendingSettingsConfiguration : IEntityTypeConfiguration<SendingSettings>
{
    public void Configure(EntityTypeBuilder<SendingSettings> builder)
    {
        builder.ToTable("sending_settings");
        builder.Property(x => x.DefaultFromName).HasMaxLength(200);
        builder.Property(x => x.DefaultReplyTo).HasMaxLength(256);
        builder.Property(x => x.OrganisationName).HasMaxLength(200);
        builder.Property(x => x.PostalAddress).HasMaxLength(500);
        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}

internal sealed class SendingDomainConfiguration : IEntityTypeConfiguration<SendingDomain>
{
    public void Configure(EntityTypeBuilder<SendingDomain> builder)
    {
        builder.ToTable("sending_domains");
        builder.Property(x => x.Domain).HasMaxLength(256);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.RecordsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.Domain }).IsUnique();
    }
}

internal sealed class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("forms");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Slug).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.FieldsJson).HasColumnType("jsonb");
        builder.Property(x => x.SettingsJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}

internal sealed class FormResponseEntryConfiguration : IEntityTypeConfiguration<FormResponseEntry>
{
    public void Configure(EntityTypeBuilder<FormResponseEntry> builder)
    {
        builder.ToTable("form_responses");
        builder.Property(x => x.AnswersJson).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.FormId, x.SubmittedAt });
    }
}
