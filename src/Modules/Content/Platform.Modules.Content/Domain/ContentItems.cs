using Platform.SharedKernel.Domain;

namespace Platform.Modules.Content.Domain;

public enum ContentStatus
{
    Draft,
    Scheduled,
    Published,
    Archived,
}

/// <summary>Search-engine and social-sharing metadata, stored as inline columns.</summary>
public sealed record Seo
{
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public string? OgImageUrl { get; init; }
    public bool NoIndex { get; init; }

    public static Seo Empty { get; } = new();
}

/// <summary>
/// Shared publishing workflow: Draft → (Scheduled) → Published → Archived.
/// The scheduler publishes Scheduled items when <see cref="ScheduledFor"/> passes.
/// </summary>
public abstract class PublishableContent : TenantAggregateRoot
{
    public string Title { get; protected set; } = null!;
    public string Slug { get; protected set; } = null!;
    public ContentStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public Seo Seo { get; protected set; } = Seo.Empty;

    public void Publish(DateTimeOffset now)
    {
        Status = ContentStatus.Published;
        PublishedAt ??= now;
        ScheduledFor = null;
    }

    public void Schedule(DateTimeOffset at, DateTimeOffset now)
    {
        if (at <= now)
        {
            Publish(now);
            return;
        }

        Status = ContentStatus.Scheduled;
        ScheduledFor = at;
    }

    public void Unpublish()
    {
        Status = ContentStatus.Draft;
        ScheduledFor = null;
    }

    public void Archive() => Status = ContentStatus.Archived;
}

/// <summary>
/// A website page built from structured blocks (hero, rich text, gallery, call-to-action…).
/// Blocks are JSON so the admin page builder can evolve without schema migrations;
/// the website renders each block type with its own component.
/// </summary>
public sealed class Page : PublishableContent
{
    private Page() { }

    public Guid? ParentId { get; private set; }

    /// <summary>Full URL path, e.g. "/about/leadership". Unique per tenant.</summary>
    public string Path { get; private set; } = null!;

    public string? Summary { get; private set; }
    public string Blocks { get; private set; } = "[]";
    public string Template { get; private set; } = "default";
    public bool ShowInNavigation { get; private set; }
    public int SortOrder { get; private set; }

    public static Page Create(string title, string slug) => new() { Title = title.Trim(), Slug = slug, Path = "/" + slug };

    public void Update(string title, string slug, Guid? parentId, string path, string? summary, string blocksJson, string template,
        bool showInNavigation, int sortOrder, Seo? seo)
    {
        if (parentId == Id)
        {
            throw new DomainException("A page cannot be its own parent.");
        }

        Title = title.Trim();
        Slug = slug;
        ParentId = parentId;
        Path = path;
        Summary = summary;
        Blocks = blocksJson;
        Template = template;
        ShowInNavigation = showInNavigation;
        SortOrder = sortOrder;
        Seo = seo ?? Seo.Empty;
    }

    public void MovePath(string path) => Path = path;
}

/// <summary>News article / blog post / devotional.</summary>
public sealed class Post : PublishableContent
{
    private Post() { }

    public string? Excerpt { get; private set; }
    public string Body { get; private set; } = "[]";
    public string? CoverImageUrl { get; private set; }
    public string? AuthorName { get; private set; }
    public string? Category { get; private set; }
    public List<string> Tags { get; private set; } = [];
    public bool IsFeatured { get; private set; }

    public static Post Create(string title, string slug) => new() { Title = title.Trim(), Slug = slug };

    public void Update(string title, string slug, string? excerpt, string bodyJson, string? coverImageUrl, string? authorName,
        string? category, IEnumerable<string>? tags, bool isFeatured, Seo? seo)
    {
        Title = title.Trim();
        Slug = slug;
        Excerpt = excerpt;
        Body = bodyJson;
        CoverImageUrl = coverImageUrl;
        AuthorName = authorName;
        Category = category?.Trim().ToLowerInvariant();
        Tags = (tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        IsFeatured = isFeatured;
        Seo = seo ?? Seo.Empty;
    }
}

/// <summary>A sermon series grouping messages (e.g. "Faith That Works — James").</summary>
public sealed class SermonSeries : TenantAggregateRoot
{
    private SermonSeries() { }

    public string Title { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateOnly? StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }

    public static SermonSeries Create(string title, string slug) => new() { Title = title.Trim(), Slug = slug };

    public void Update(string title, string slug, string? description, string? imageUrl, DateOnly? startsOn, DateOnly? endsOn)
    {
        Title = title.Trim();
        Slug = slug;
        Description = description;
        ImageUrl = imageUrl;
        StartsOn = startsOn;
        EndsOn = endsOn;
    }
}

/// <summary>A preached message with video/audio/notes — the most visited content on church sites and apps.</summary>
public sealed class Sermon : PublishableContent
{
    private Sermon() { }

    public Guid? SeriesId { get; private set; }
    public string Preacher { get; private set; } = null!;

    /// <summary>People module id of the preacher when they are a member.</summary>
    public Guid? PreacherPersonId { get; private set; }

    public DateOnly PreachedOn { get; private set; }
    public List<string> ScriptureReferences { get; private set; } = [];
    public string? Summary { get; private set; }
    public string? Notes { get; private set; }
    public string? VideoUrl { get; private set; }
    public string? AudioUrl { get; private set; }
    public string? NotesDocumentUrl { get; private set; }
    public string? ThumbnailUrl { get; private set; }
    public int? DurationSeconds { get; private set; }
    public List<string> Tags { get; private set; } = [];

    /// <summary>Events module occurrence where it was preached (links attendance and giving).</summary>
    public Guid? OccurrenceId { get; private set; }

    public static Sermon Create(string title, string slug, string preacher, DateOnly preachedOn) =>
        new() { Title = title.Trim(), Slug = slug, Preacher = preacher.Trim(), PreachedOn = preachedOn };

    public void Update(SermonDetails d, string slug)
    {
        Title = d.Title.Trim();
        Slug = slug;
        SeriesId = d.SeriesId;
        Preacher = d.Preacher.Trim();
        PreacherPersonId = d.PreacherPersonId;
        PreachedOn = d.PreachedOn;
        ScriptureReferences = (d.ScriptureReferences ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        Summary = d.Summary;
        Notes = d.Notes;
        VideoUrl = d.VideoUrl;
        AudioUrl = d.AudioUrl;
        NotesDocumentUrl = d.NotesDocumentUrl;
        ThumbnailUrl = d.ThumbnailUrl;
        DurationSeconds = d.DurationSeconds;
        Tags = (d.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        OccurrenceId = d.OccurrenceId;
        Seo = d.Seo ?? Seo.Empty;
    }
}

public sealed record SermonDetails(
    string Title, Guid? SeriesId, string Preacher, Guid? PreacherPersonId, DateOnly PreachedOn, IReadOnlyList<string>? ScriptureReferences,
    string? Summary, string? Notes, string? VideoUrl, string? AudioUrl, string? NotesDocumentUrl, string? ThumbnailUrl, int? DurationSeconds,
    IReadOnlyList<string>? Tags, Guid? OccurrenceId, Seo? Seo);

/// <summary>An uploaded file in the media library (images, audio, PDFs).</summary>
public sealed class MediaAsset : TenantAggregateRoot
{
    private MediaAsset() { }

    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string Url { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string? AltText { get; private set; }
    public string? Folder { get; private set; }

    public static MediaAsset Create(string fileName, string storageKey, string url, string contentType, long size, string? folder) =>
        new() { FileName = fileName, StorageKey = storageKey, Url = url, ContentType = contentType, SizeBytes = size, Folder = folder };

    public void Describe(string? altText, string? folder)
    {
        AltText = altText;
        Folder = folder;
    }
}

/// <summary>Navigation menu (e.g. "main", "footer") as a JSON tree of { label, url, pageId?, children[] }.</summary>
public sealed class Menu : TenantAggregateRoot
{
    private Menu() { }

    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Items { get; private set; } = "[]";

    public static Menu Create(string key, string name) => new() { Key = key, Name = name };

    public void Update(string name, string itemsJson)
    {
        Name = name;
        Items = itemsJson;
    }
}
