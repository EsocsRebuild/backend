using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Content.Domain;

namespace Platform.Modules.Content.Infrastructure.Configurations;

internal static class PublishableConfiguration
{
    public static void ConfigurePublishable<T>(this EntityTypeBuilder<T> builder) where T : PublishableContent
    {
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Slug).HasMaxLength(200).IsCaseInsensitive();
        builder.Property(x => x.Status).IsEnumText();
        builder.ComplexProperty(x => x.Seo, s =>
        {
            s.Property(p => p.MetaTitle).HasMaxLength(200);
            s.Property(p => p.MetaDescription).HasMaxLength(500);
            s.Property(p => p.OgImageUrl).HasMaxLength(1024);
        });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.PublishedAt });
        builder.HasIndex(x => new { x.Status, x.ScheduledFor }).HasFilter("status = 'Scheduled'");
    }
}

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.ToTable("pages");
        builder.ConfigurePublishable();
        builder.Property(x => x.Path).HasMaxLength(500).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Path }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Summary).HasMaxLength(1000);
        builder.Property(x => x.Blocks).HasColumnType("jsonb");
        builder.Property(x => x.Template).HasMaxLength(64);
        builder.HasOne<Page>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("posts");
        builder.ConfigurePublishable();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Excerpt).HasMaxLength(1000);
        builder.Property(x => x.Body).HasColumnType("jsonb");
        builder.Property(x => x.CoverImageUrl).HasMaxLength(1024);
        builder.Property(x => x.AuthorName).HasMaxLength(200);
        builder.Property(x => x.Category).HasMaxLength(64);
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.HasIndex(x => x.Tags).HasMethod("gin");
    }
}

internal sealed class SeriesConfiguration : IEntityTypeConfiguration<SermonSeries>
{
    public void Configure(EntityTypeBuilder<SermonSeries> builder)
    {
        builder.ToTable("sermon_series");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Slug).HasMaxLength(200).IsCaseInsensitive();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.ImageUrl).HasMaxLength(1024);
    }
}

internal sealed class SermonConfiguration : IEntityTypeConfiguration<Sermon>
{
    public void Configure(EntityTypeBuilder<Sermon> builder)
    {
        builder.ToTable("sermons");
        builder.ConfigurePublishable();
        builder.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique().HasFilter("is_deleted = false");
        builder.Property(x => x.Preacher).HasMaxLength(200);
        builder.Property(x => x.ScriptureReferences).HasColumnType("text[]");
        builder.Property(x => x.Summary).HasMaxLength(2000);
        builder.Property(x => x.Notes).HasMaxLength(50_000);
        builder.Property(x => x.VideoUrl).HasMaxLength(1024);
        builder.Property(x => x.AudioUrl).HasMaxLength(1024);
        builder.Property(x => x.NotesDocumentUrl).HasMaxLength(1024);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(1024);
        builder.Property(x => x.Tags).HasColumnType("text[]");
        builder.HasIndex(x => new { x.TenantId, x.PreachedOn });
        builder.HasIndex(x => x.SeriesId);
        builder.HasOne<SermonSeries>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class MediaConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets");
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.StorageKey).HasMaxLength(512);
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.Property(x => x.Url).HasMaxLength(1024);
        builder.Property(x => x.ContentType).HasMaxLength(128);
        builder.Property(x => x.AltText).HasMaxLength(500);
        builder.Property(x => x.Folder).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.Folder });
    }
}

internal sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus");
        builder.Property(x => x.Key).HasMaxLength(64).IsCaseInsensitive();
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.Items).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.Key }).IsUnique().HasFilter("is_deleted = false");
    }
}
