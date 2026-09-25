using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Modules.Content.Domain;
using Platform.Modules.Content.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Content.Features;

public sealed record SavePageRequest(string Title, string? Slug, Guid? ParentId, string? Summary, JsonElement? Blocks, string? Template,
    bool ShowInNavigation, int SortOrder, Seo? Seo);

public sealed record PageResponse(Guid Id, string Title, string Slug, string Path, Guid? ParentId, string? Summary, JsonElement Blocks,
    string Template, bool ShowInNavigation, int SortOrder, Seo Seo, string Status, DateTimeOffset? PublishedAt, DateTimeOffset? ScheduledFor,
    DateTimeOffset? UpdatedAt);

public sealed record SavePostRequest(string Title, string? Slug, string? Excerpt, JsonElement? Body, string? CoverImageUrl, string? AuthorName,
    string? Category, IReadOnlyList<string>? Tags, bool IsFeatured, Seo? Seo);

public sealed record PostResponse(Guid Id, string Title, string Slug, string? Excerpt, JsonElement Body, string? CoverImageUrl, string? AuthorName,
    string? Category, IReadOnlyList<string> Tags, bool IsFeatured, Seo Seo, string Status, DateTimeOffset? PublishedAt, DateTimeOffset? ScheduledFor);

public sealed record SaveSermonRequest(string Title, string? Slug, Guid? SeriesId, string Preacher, Guid? PreacherPersonId, DateOnly PreachedOn,
    IReadOnlyList<string>? ScriptureReferences, string? Summary, string? Notes, string? VideoUrl, string? AudioUrl, string? NotesDocumentUrl,
    string? ThumbnailUrl, int? DurationSeconds, IReadOnlyList<string>? Tags, Guid? OccurrenceId, Seo? Seo);

public sealed record SermonResponse(Guid Id, string Title, string Slug, Guid? SeriesId, string? SeriesTitle, string Preacher, Guid? PreacherPersonId,
    DateOnly PreachedOn, IReadOnlyList<string> ScriptureReferences, string? Summary, string? Notes, string? VideoUrl, string? AudioUrl,
    string? NotesDocumentUrl, string? ThumbnailUrl, int? DurationSeconds, IReadOnlyList<string> Tags, string Status, DateTimeOffset? PublishedAt);

public sealed record SaveSeriesRequest(string Title, string? Slug, string? Description, string? ImageUrl, DateOnly? StartsOn, DateOnly? EndsOn);

public sealed record SeriesResponse(Guid Id, string Title, string Slug, string? Description, string? ImageUrl, DateOnly? StartsOn, DateOnly? EndsOn, int SermonCount);

public sealed record PublishRequest(DateTimeOffset? ScheduledFor);

public sealed record MediaResponse(Guid Id, string FileName, string Url, string ContentType, long SizeBytes, string? AltText, string? Folder, DateTimeOffset CreatedAt);

public sealed record DescribeMediaRequest(string? AltText, string? Folder);

public sealed record SaveMenuRequest(string Name, JsonElement Items);

public sealed record MenuResponse(Guid Id, string Key, string Name, JsonElement Items);

public sealed record ContentQuery(int Page = 1, int PageSize = 25, string? Search = null, ContentStatus? Status = null, string? Tag = null,
    string? Category = null, Guid? SeriesId = null);

internal static class ContentRules
{
    public const int MaxJsonBytes = 512 * 1024;

    public static bool BeJsonArray(JsonElement? e) => e is null || (e.Value.ValueKind == JsonValueKind.Array && e.Value.GetRawText().Length <= MaxJsonBytes);

    public static bool BeValidSlug(string? slug) => slug is null || Slug.IsValid(slug);

    public static bool BeHttpUrl(string? url) => url is null || (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "https" or "http");
}

internal sealed class SavePageValidator : AbstractValidator<SavePageRequest>
{
    public SavePageValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(ContentRules.BeValidSlug).WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Blocks).Must(ContentRules.BeJsonArray).WithMessage("Blocks must be a JSON array (max 512 KB).");
        RuleFor(x => x.Template).MaximumLength(64);
        RuleFor(x => x.Summary).MaximumLength(1000);
    }
}

internal sealed class SavePostValidator : AbstractValidator<SavePostRequest>
{
    public SavePostValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(ContentRules.BeValidSlug).WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Body).Must(ContentRules.BeJsonArray).WithMessage("Body must be a JSON array of blocks (max 512 KB).");
        RuleFor(x => x.Excerpt).MaximumLength(1000);
        RuleFor(x => x.CoverImageUrl).MaximumLength(1024);
        RuleFor(x => x.Category).MaximumLength(64);
    }
}

internal sealed class SaveSermonValidator : AbstractValidator<SaveSermonRequest>
{
    public SaveSermonValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(ContentRules.BeValidSlug).WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Preacher).NotEmpty().MaximumLength(200);
        RuleFor(x => x.VideoUrl).Must(ContentRules.BeHttpUrl).WithMessage("Must be an absolute URL.");
        RuleFor(x => x.AudioUrl).Must(ContentRules.BeHttpUrl).WithMessage("Must be an absolute URL.");
        RuleFor(x => x.Notes).MaximumLength(50_000);
        RuleFor(x => x.DurationSeconds).GreaterThan(0).When(x => x.DurationSeconds.HasValue);
    }
}

/// <summary>Website &amp; app content management: pages, posts, sermons, media and menus.</summary>
public static class ContentAdminEndpoints
{
    private const long MaxUploadBytes = 50 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMedia = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/svg+xml", "audio/mpeg", "audio/mp4", "audio/aac",
        "video/mp4", "application/pdf",
    };

    private static readonly Error NotFound = Error.NotFound("content.not_found", "The content item was not found.");
    private static readonly Error SlugTaken = Error.Conflict("content.slug_taken", "Another item already uses this URL.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var pages = endpoints.MapModuleGroup("content/pages", "Website content");
        pages.MapGet("/", ListPages).RequirePermission(Permissions.Content.Read).WithSummary("List pages");
        pages.MapGet("/{id:guid}", GetPage).RequirePermission(Permissions.Content.Read).WithSummary("Get a page");
        pages.MapPost("/", CreatePage).WithValidation<SavePageRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Create a page (draft)");
        pages.MapPut("/{id:guid}", UpdatePage).WithValidation<SavePageRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Update a page");
        MapLifecycle<Page>(pages);

        var posts = endpoints.MapModuleGroup("content/posts", "Website content");
        posts.MapGet("/", ListPosts).RequirePermission(Permissions.Content.Read).WithSummary("List news & blog posts");
        posts.MapGet("/{id:guid}", GetPost).RequirePermission(Permissions.Content.Read).WithSummary("Get a post");
        posts.MapPost("/", CreatePost).WithValidation<SavePostRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Create a post (draft)");
        posts.MapPut("/{id:guid}", UpdatePost).WithValidation<SavePostRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Update a post");
        MapLifecycle<Post>(posts);

        var sermons = endpoints.MapModuleGroup("content/sermons", "Sermons");
        sermons.MapGet("/", ListSermons).RequirePermission(Permissions.Content.Read).WithSummary("List sermons");
        sermons.MapGet("/{id:guid}", GetSermon).RequirePermission(Permissions.Content.Read).WithSummary("Get a sermon");
        sermons.MapPost("/", CreateSermon).WithValidation<SaveSermonRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Add a sermon (draft)");
        sermons.MapPut("/{id:guid}", UpdateSermon).WithValidation<SaveSermonRequest>().RequirePermission(Permissions.Content.Write).WithSummary("Update a sermon");
        MapLifecycle<Sermon>(sermons);

        var series = endpoints.MapModuleGroup("content/series", "Sermons");
        series.MapGet("/", ListSeries).RequirePermission(Permissions.Content.Read).WithSummary("Sermon series");
        series.MapPost("/", (SaveSeriesRequest r, ContentDbContext db, CancellationToken ct) => SaveSeries(null, r, db, ct))
            .RequirePermission(Permissions.Content.Write).WithSummary("Create a series");
        series.MapPut("/{id:guid}", (Guid id, SaveSeriesRequest r, ContentDbContext db, CancellationToken ct) => SaveSeries(id, r, db, ct))
            .RequirePermission(Permissions.Content.Write).WithSummary("Update a series");

        var media = endpoints.MapModuleGroup("content/media", "Media library");
        media.MapGet("/", ListMedia).RequirePermission(Permissions.Content.Read).WithSummary("Browse the media library");
        media.MapPost("/", Upload).RequirePermission(Permissions.Content.MediaManage).DisableAntiforgery()
            .WithSummary("Upload a file (multipart/form-data, max 50 MB)");
        media.MapPut("/{id:guid}", DescribeMedia).RequirePermission(Permissions.Content.MediaManage).WithSummary("Set alt text / folder");
        media.MapDelete("/{id:guid}", DeleteMedia).RequirePermission(Permissions.Content.MediaManage).WithSummary("Delete a file");

        var menus = endpoints.MapModuleGroup("content/menus", "Website content");
        menus.MapGet("/", async (ContentDbContext db, CancellationToken ct) => Results.Ok((await db.Menus.AsNoTracking().OrderBy(m => m.Key).ToListAsync(ct)).Select(ToResponse)))
            .RequirePermission(Permissions.Content.Read).WithSummary("List menus");
        menus.MapPut("/{key}", SaveMenu).RequirePermission(Permissions.Content.MenusManage).WithSummary("Create or replace a menu (e.g. main, footer)");
    }

    /// <summary>Publish / schedule / unpublish / archive / delete — identical for every publishable type.</summary>
    private static void MapLifecycle<T>(RouteGroupBuilder group) where T : PublishableContent
    {
        group.MapPost("/{id:guid}/publish", async (Guid id, PublishRequest? r, ContentDbContext db, TimeProvider clock, CancellationToken ct) =>
                await Mutate<T>(id, db, x =>
                {
                    if (r?.ScheduledFor is { } at) x.Schedule(at, clock.GetUtcNow());
                    else x.Publish(clock.GetUtcNow());
                }, ct))
            .RequirePermission(Permissions.Content.Publish).WithSummary("Publish now or schedule (scheduledFor)");
        group.MapPost("/{id:guid}/unpublish", async (Guid id, ContentDbContext db, CancellationToken ct) => await Mutate<T>(id, db, x => x.Unpublish(), ct))
            .RequirePermission(Permissions.Content.Publish).WithSummary("Revert to draft");
        group.MapPost("/{id:guid}/archive", async (Guid id, ContentDbContext db, CancellationToken ct) => await Mutate<T>(id, db, x => x.Archive(), ct))
            .RequirePermission(Permissions.Content.Publish).WithSummary("Archive");
        group.MapDelete("/{id:guid}", async (Guid id, ContentDbContext db, CancellationToken ct) =>
            {
                var item = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (item is null)
                {
                    return NotFound.ToProblem();
                }

                db.Remove(item);
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            })
            .RequirePermission(Permissions.Content.Delete).WithSummary("Delete (soft)");
    }

    private static async Task<IResult> Mutate<T>(Guid id, ContentDbContext db, Action<T> action, CancellationToken ct) where T : PublishableContent
    {
        var item = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null)
        {
            return NotFound.ToProblem();
        }

        action(item);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { item.Id, Status = item.Status.ToString(), item.PublishedAt, item.ScheduledFor });
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    // ---- Pages -----------------------------------------------------------------------------

    internal static PageResponse ToResponse(Page p) => new(p.Id, p.Title, p.Slug, p.Path, p.ParentId, p.Summary, Json(p.Blocks), p.Template,
        p.ShowInNavigation, p.SortOrder, p.Seo, p.Status.ToString(), p.PublishedAt, p.ScheduledFor, p.UpdatedAt ?? p.CreatedAt);

    private static async Task<IResult> ListPages([AsParameters] ContentQuery q, ContentDbContext db, CancellationToken ct)
    {
        var query = db.Pages.AsNoTracking();
        if (q.Status is { } s) query = query.Where(p => p.Status == s);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(p => EF.Functions.ILike(p.Title, $"%{q.Search.Trim()}%"));
        return Results.Ok((await query.OrderBy(p => p.Path).Take(500).ToListAsync(ct)).Select(ToResponse));
    }

    private static async Task<IResult> GetPage(Guid id, ContentDbContext db, CancellationToken ct) =>
        await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } p ? Results.Ok(ToResponse(p)) : NotFound.ToProblem();

    private static Task<IResult> CreatePage(SavePageRequest r, ContentDbContext db, CancellationToken ct) => SavePage(null, r, db, ct);

    private static Task<IResult> UpdatePage(Guid id, SavePageRequest r, ContentDbContext db, CancellationToken ct) => SavePage(id, r, db, ct);

    private static async Task<IResult> SavePage(Guid? id, SavePageRequest r, ContentDbContext db, CancellationToken ct)
    {
        var slug = r.Slug ?? Slug.From(r.Title);
        var parentPath = string.Empty;
        if (r.ParentId is { } parentId)
        {
            parentPath = await db.Pages.Where(p => p.Id == parentId).Select(p => p.Path).FirstOrDefaultAsync(ct) ?? "";
            if (parentPath.Length == 0)
            {
                return Error.Validation("page.invalid_parent", "The parent page does not exist.").ToProblem();
            }
        }

        var path = slug == "home" && r.ParentId is null ? "/" : $"{parentPath.TrimEnd('/')}/{slug}";
        if (await db.Pages.AnyAsync(p => p.Path == path && p.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        Page page;
        if (id is null)
        {
            page = Page.Create(r.Title, slug);
            db.Pages.Add(page);
        }
        else
        {
            var existing = await db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (existing is null)
            {
                return NotFound.ToProblem();
            }

            page = existing;
        }

        var oldPath = page.Path;
        page.Update(r.Title, slug, r.ParentId, path, r.Summary, r.Blocks?.GetRawText() ?? page.Blocks, r.Template ?? "default",
            r.ShowInNavigation, r.SortOrder, r.Seo);

        // Keep descendants' paths consistent when a page moves or is renamed.
        if (id is not null && oldPath != path)
        {
            var descendants = await db.Pages.Where(p => p.Path.StartsWith(oldPath + "/")).ToListAsync(ct);
            descendants.ForEach(d => d.MovePath(path + d.Path[oldPath.Length..]));
        }

        await db.SaveChangesAsync(ct);
        return id is null ? Results.Created($"/api/v1/content/pages/{page.Id}", ToResponse(page)) : Results.Ok(ToResponse(page));
    }

    // ---- Posts -----------------------------------------------------------------------------

    internal static PostResponse ToResponse(Post p) => new(p.Id, p.Title, p.Slug, p.Excerpt, Json(p.Body), p.CoverImageUrl, p.AuthorName, p.Category,
        p.Tags, p.IsFeatured, p.Seo, p.Status.ToString(), p.PublishedAt, p.ScheduledFor);

    private static async Task<IResult> ListPosts([AsParameters] ContentQuery q, ContentDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Posts.AsNoTracking();
        if (q.Status is { } s) query = query.Where(p => p.Status == s);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(p => EF.Functions.ILike(p.Title, $"%{q.Search.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(q.Tag)) query = query.Where(p => p.Tags.Contains(q.Tag.ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(p => p.Category == q.Category.ToLowerInvariant());

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(p => p.PublishedAt ?? p.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<PostResponse>(items.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> GetPost(Guid id, ContentDbContext db, CancellationToken ct) =>
        await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } p ? Results.Ok(ToResponse(p)) : NotFound.ToProblem();

    private static Task<IResult> CreatePost(SavePostRequest r, ContentDbContext db, CancellationToken ct) => SavePost(null, r, db, ct);

    private static Task<IResult> UpdatePost(Guid id, SavePostRequest r, ContentDbContext db, CancellationToken ct) => SavePost(id, r, db, ct);

    private static async Task<IResult> SavePost(Guid? id, SavePostRequest r, ContentDbContext db, CancellationToken ct)
    {
        var slug = r.Slug ?? Slug.From(r.Title);
        if (await db.Posts.AnyAsync(p => p.Slug == slug && p.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        Post? post = id is null ? Post.Create(r.Title, slug) : await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
        {
            return NotFound.ToProblem();
        }

        if (id is null)
        {
            db.Posts.Add(post);
        }

        post.Update(r.Title, slug, r.Excerpt, r.Body?.GetRawText() ?? post.Body, r.CoverImageUrl, r.AuthorName, r.Category, r.Tags, r.IsFeatured, r.Seo);
        await db.SaveChangesAsync(ct);
        return id is null ? Results.Created($"/api/v1/content/posts/{post.Id}", ToResponse(post)) : Results.Ok(ToResponse(post));
    }

    // ---- Sermons ---------------------------------------------------------------------------

    internal static SermonResponse ToResponse(Sermon s, string? seriesTitle) => new(s.Id, s.Title, s.Slug, s.SeriesId, seriesTitle, s.Preacher,
        s.PreacherPersonId, s.PreachedOn, s.ScriptureReferences, s.Summary, s.Notes, s.VideoUrl, s.AudioUrl, s.NotesDocumentUrl, s.ThumbnailUrl,
        s.DurationSeconds, s.Tags, s.Status.ToString(), s.PublishedAt);

    private static async Task<IResult> ListSermons([AsParameters] ContentQuery q, ContentDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Sermons.AsNoTracking();
        if (q.Status is { } s) query = query.Where(x => x.Status == s);
        if (q.SeriesId is { } seriesId) query = query.Where(x => x.SeriesId == seriesId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = $"%{q.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Title, pattern) || EF.Functions.ILike(x.Preacher, pattern));
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(x => x.PreachedOn).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var titles = await SeriesTitlesAsync(db, items, ct);
        return Results.Ok(new PagedResult<SermonResponse>(items.Select(x => ToResponse(x, x.SeriesId is { } sid ? titles.GetValueOrDefault(sid) : null)).ToList(),
            page.SafePage, page.SafePageSize, total));
    }

    internal static async Task<Dictionary<Guid, string>> SeriesTitlesAsync(ContentDbContext db, IEnumerable<Sermon> sermons, CancellationToken ct)
    {
        var ids = sermons.Where(s => s.SeriesId != null).Select(s => s.SeriesId!.Value).Distinct().ToList();
        return await db.Series.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Title, ct);
    }

    private static async Task<IResult> GetSermon(Guid id, ContentDbContext db, CancellationToken ct)
    {
        var sermon = await db.Sermons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (sermon is null)
        {
            return NotFound.ToProblem();
        }

        var titles = await SeriesTitlesAsync(db, [sermon], ct);
        return Results.Ok(ToResponse(sermon, sermon.SeriesId is { } sid ? titles.GetValueOrDefault(sid) : null));
    }

    private static Task<IResult> CreateSermon(SaveSermonRequest r, ContentDbContext db, CancellationToken ct) => SaveSermon(null, r, db, ct);

    private static Task<IResult> UpdateSermon(Guid id, SaveSermonRequest r, ContentDbContext db, CancellationToken ct) => SaveSermon(id, r, db, ct);

    private static async Task<IResult> SaveSermon(Guid? id, SaveSermonRequest r, ContentDbContext db, CancellationToken ct)
    {
        var slug = r.Slug ?? Slug.From($"{r.PreachedOn:yyyy-MM-dd} {r.Title}");
        if (await db.Sermons.AnyAsync(s => s.Slug == slug && s.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        if (r.SeriesId is { } seriesId && !await db.Series.AnyAsync(s => s.Id == seriesId, ct))
        {
            return Error.Validation("sermon.invalid_series", "The series does not exist.").ToProblem();
        }

        Sermon? sermon = id is null ? Sermon.Create(r.Title, slug, r.Preacher, r.PreachedOn) : await db.Sermons.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sermon is null)
        {
            return NotFound.ToProblem();
        }

        if (id is null)
        {
            db.Sermons.Add(sermon);
        }

        sermon.Update(new SermonDetails(r.Title, r.SeriesId, r.Preacher, r.PreacherPersonId, r.PreachedOn, r.ScriptureReferences, r.Summary, r.Notes,
            r.VideoUrl, r.AudioUrl, r.NotesDocumentUrl, r.ThumbnailUrl, r.DurationSeconds, r.Tags, r.OccurrenceId, r.Seo), slug);
        await db.SaveChangesAsync(ct);
        return id is null ? Results.Created($"/api/v1/content/sermons/{sermon.Id}", ToResponse(sermon, null)) : Results.Ok(ToResponse(sermon, null));
    }

    private static async Task<IResult> ListSeries(ContentDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Series.AsNoTracking().OrderByDescending(s => s.StartsOn)
            .Select(s => new SeriesResponse(s.Id, s.Title, s.Slug, s.Description, s.ImageUrl, s.StartsOn, s.EndsOn, db.Sermons.Count(x => x.SeriesId == s.Id)))
            .ToListAsync(ct));

    private static async Task<IResult> SaveSeries(Guid? id, SaveSeriesRequest r, ContentDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || !ContentRules.BeValidSlug(r.Slug))
        {
            return Error.Validation("series.invalid", "A title and a valid slug are required.").ToProblem();
        }

        var slug = r.Slug ?? Slug.From(r.Title);
        if (await db.Series.AnyAsync(s => s.Slug == slug && s.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        SermonSeries? series = id is null ? SermonSeries.Create(r.Title, slug) : await db.Series.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (series is null)
        {
            return NotFound.ToProblem();
        }

        if (id is null)
        {
            db.Series.Add(series);
        }

        series.Update(r.Title, slug, r.Description, r.ImageUrl, r.StartsOn, r.EndsOn);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SeriesResponse(series.Id, series.Title, series.Slug, series.Description, series.ImageUrl, series.StartsOn, series.EndsOn, 0));
    }

    // ---- Media -----------------------------------------------------------------------------

    private static MediaResponse ToResponse(MediaAsset m) => new(m.Id, m.FileName, m.Url, m.ContentType, m.SizeBytes, m.AltText, m.Folder, m.CreatedAt);

    private static async Task<IResult> ListMedia([AsParameters] PageRequest page, string? folder, string? type, ContentDbContext db, CancellationToken ct)
    {
        var query = db.Media.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(folder)) query = query.Where(m => m.Folder == folder);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.ContentType.StartsWith(type));
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(m => m.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<MediaResponse>(items.Select(ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Upload(IFormFile file, string? folder, ContentDbContext db, IFileStorage storage, ITenantContext tenant, CancellationToken ct)
    {
        if (file.Length is 0 or > MaxUploadBytes)
        {
            return Error.Validation("media.size", "Files must be between 1 byte and 50 MB.").ToProblem();
        }

        if (!AllowedMedia.Contains(file.ContentType))
        {
            return Error.Validation("media.type", $"Unsupported file type '{file.ContentType}'.").ToProblem();
        }

        // Never trust the client file name for the storage path.
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        extension = extension.Length is > 1 and <= 6 && extension[1..].All(char.IsAsciiLetterOrDigit) ? extension : string.Empty;
        var key = $"{tenant.RequiredTenantId:N}/{DateTime.UtcNow:yyyy/MM}/{Guid.CreateVersion7():N}{extension}";

        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, key, file.ContentType, ct);

        var asset = MediaAsset.Create(Path.GetFileName(file.FileName), stored.Key, stored.Url, file.ContentType, stored.Size, folder);
        db.Media.Add(asset);
        await db.SaveChangesAsync(ct);
        return Results.Created(stored.Url, ToResponse(asset));
    }

    private static async Task<IResult> DescribeMedia(Guid id, DescribeMediaRequest r, ContentDbContext db, CancellationToken ct)
    {
        var asset = await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (asset is null)
        {
            return NotFound.ToProblem();
        }

        asset.Describe(r.AltText, r.Folder);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(asset));
    }

    private static async Task<IResult> DeleteMedia(Guid id, ContentDbContext db, IFileStorage storage, CancellationToken ct)
    {
        var asset = await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (asset is null)
        {
            return NotFound.ToProblem();
        }

        db.Media.Remove(asset);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(asset.StorageKey, ct);
        return Results.NoContent();
    }

    // ---- Menus -----------------------------------------------------------------------------

    internal static MenuResponse ToResponse(Menu m) => new(m.Id, m.Key, m.Name, Json(m.Items));

    private static async Task<IResult> SaveMenu(string key, SaveMenuRequest r, ContentDbContext db, CancellationToken ct)
    {
        if (!Slug.IsValid(key) || key.Length > 64 || r.Items.ValueKind != JsonValueKind.Array || string.IsNullOrWhiteSpace(r.Name))
        {
            return Error.Validation("menu.invalid", "A valid key, a name and an items array are required.").ToProblem();
        }

        var menu = await db.Menus.FirstOrDefaultAsync(m => m.Key == key, ct);
        if (menu is null)
        {
            menu = Menu.Create(key, r.Name);
            db.Menus.Add(menu);
        }

        menu.Update(r.Name, r.Items.GetRawText());
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(menu));
    }
}
