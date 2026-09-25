using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Modules.Content.Domain;
using Platform.Modules.Content.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Content.Features;

/// <summary>
/// Read-only delivery API for the website and mobile app. Only Published content is visible;
/// responses are cacheable at the CDN edge for a short time.
/// </summary>
public static class PublicContentEndpoints
{
    private static readonly Error NotFound = Error.NotFound("content.not_found", "Not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapPublicGroup("content", "Public");

        group.MapGet("/pages", async (HttpContext http, string path, ContentDbContext db, CancellationToken ct) =>
            {
                var normalized = "/" + path.Trim().Trim('/');
                var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Path == normalized && p.Status == ContentStatus.Published, ct);
                Cache(http);
                return page is null ? NotFound.ToProblem() : Results.Ok(ContentAdminEndpoints.ToResponse(page));
            })
            .WithSummary("Get a published page by path, e.g. ?path=/about/leadership");

        group.MapGet("/navigation", async (HttpContext http, ContentDbContext db, CancellationToken ct) =>
            {
                Cache(http);
                return Results.Ok(await db.Pages.AsNoTracking()
                    .Where(p => p.Status == ContentStatus.Published && p.ShowInNavigation)
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                    .Select(p => new { p.Id, p.Title, p.Path, p.ParentId })
                    .ToListAsync(ct));
            })
            .WithSummary("Pages flagged for navigation");

        group.MapGet("/menus/{key}", async (string key, HttpContext http, ContentDbContext db, CancellationToken ct) =>
            {
                var menu = await db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.Key == key, ct);
                Cache(http);
                return menu is null ? NotFound.ToProblem() : Results.Ok(ContentAdminEndpoints.ToResponse(menu));
            })
            .WithSummary("A navigation menu by key");

        group.MapGet("/posts", async ([AsParameters] PageRequest page, string? tag, string? category, bool? featured, HttpContext http,
                ContentDbContext db, CancellationToken ct) =>
            {
                var query = db.Posts.AsNoTracking().Where(p => p.Status == ContentStatus.Published);
                if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(p => p.Tags.Contains(tag.ToLowerInvariant()));
                if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category.ToLowerInvariant());
                if (featured == true) query = query.Where(p => p.IsFeatured);

                var total = await query.LongCountAsync(ct);
                var items = await query.OrderByDescending(p => p.PublishedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
                Cache(http);
                return Results.Ok(new PagedResult<PostResponse>(items.Select(ContentAdminEndpoints.ToResponse).ToList(), page.SafePage, page.SafePageSize, total));
            })
            .WithSummary("Published news & blog posts");

        group.MapGet("/posts/{slug}", async (string slug, HttpContext http, ContentDbContext db, CancellationToken ct) =>
            {
                var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.Status == ContentStatus.Published, ct);
                Cache(http);
                return post is null ? NotFound.ToProblem() : Results.Ok(ContentAdminEndpoints.ToResponse(post));
            })
            .WithSummary("A published post by slug");

        group.MapGet("/sermons", async ([AsParameters] PageRequest page, string? series, string? preacher, string? search, HttpContext http,
                ContentDbContext db, CancellationToken ct) =>
            {
                var query = db.Sermons.AsNoTracking().Where(s => s.Status == ContentStatus.Published);
                if (!string.IsNullOrWhiteSpace(series))
                {
                    query = query.Where(s => db.Series.Any(x => x.Id == s.SeriesId && x.Slug == series));
                }

                if (!string.IsNullOrWhiteSpace(preacher)) query = query.Where(s => EF.Functions.ILike(s.Preacher, $"%{preacher}%"));
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => EF.Functions.ILike(s.Title, $"%{search}%"));

                var total = await query.LongCountAsync(ct);
                var items = await query.OrderByDescending(s => s.PreachedOn).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
                var titles = await ContentAdminEndpoints.SeriesTitlesAsync(db, items, ct);
                Cache(http);
                return Results.Ok(new PagedResult<SermonResponse>(
                    items.Select(s => ContentAdminEndpoints.ToResponse(s, s.SeriesId is { } sid ? titles.GetValueOrDefault(sid) : null)).ToList(),
                    page.SafePage, page.SafePageSize, total));
            })
            .WithSummary("Published sermons (filter by series slug, preacher or text)");

        group.MapGet("/sermons/{slug}", async (string slug, HttpContext http, ContentDbContext db, CancellationToken ct) =>
            {
                var sermon = await db.Sermons.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug && s.Status == ContentStatus.Published, ct);
                if (sermon is null)
                {
                    return NotFound.ToProblem();
                }

                var titles = await ContentAdminEndpoints.SeriesTitlesAsync(db, [sermon], ct);
                Cache(http);
                return Results.Ok(ContentAdminEndpoints.ToResponse(sermon, sermon.SeriesId is { } sid ? titles.GetValueOrDefault(sid) : null));
            })
            .WithSummary("A published sermon by slug");

        group.MapGet("/series", async (HttpContext http, ContentDbContext db, CancellationToken ct) =>
            {
                Cache(http);
                return Results.Ok(await db.Series.AsNoTracking()
                    .Where(s => db.Sermons.Any(x => x.SeriesId == s.Id && x.Status == ContentStatus.Published))
                    .OrderByDescending(s => s.StartsOn)
                    .Select(s => new SeriesResponse(s.Id, s.Title, s.Slug, s.Description, s.ImageUrl, s.StartsOn, s.EndsOn,
                        db.Sermons.Count(x => x.SeriesId == s.Id && x.Status == ContentStatus.Published)))
                    .ToListAsync(ct));
            })
            .WithSummary("Sermon series with published messages");
    }

    private static void Cache(HttpContext http) => http.Response.Headers.CacheControl = "public, max-age=60, stale-while-revalidate=300";
}
