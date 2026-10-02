using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record FormSummaryResponse(
    Guid Id,
    string Title,
    string Slug,
    string Status,
    int ResponseCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt);

public sealed record FormDetailResponse(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    string Status,
    int ResponseCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    JsonElement? Fields,
    JsonElement? Settings);

public sealed record PublicFormResponse(
    string Slug,
    string Title,
    string? Description,
    string Status,
    JsonElement? Fields,
    JsonElement? Settings,
    string OrganisationName);

public sealed record FormResponseItem(
    Guid Id,
    DateTimeOffset SubmittedAt,
    JsonElement Answers);

public sealed record CreateFormInput(string Title);
public sealed record UpdateFormInput(string? Title, string? Description, JsonElement? Fields);
public sealed record UpdateSlugInput(string Slug);
public sealed record DeleteResponsesInput(IReadOnlyList<Guid> ResponseIds);
public sealed record SubmitResponseInput(JsonElement Answers, string? CaptchaToken = null);

public sealed record FormQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Status = null);

public static class FormEndpoints
{
    private static readonly Error NotFound = Error.NotFound("forms.not_found", "We couldn't find that form.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("forms", "Forms");

        group.MapGet("/", ListForms)
            .RequirePermission(Permissions.Forms.View)
            .WithSummary("List all forms");

        group.MapGet("/{id:guid}", GetForm)
            .RequirePermission(Permissions.Forms.View)
            .WithSummary("Get form by ID");

        group.MapPost("/", CreateForm)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Create draft form");

        group.MapPatch("/{id:guid}", UpdateForm)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Update form details");

        group.MapPut("/{id:guid}/settings", UpdateSettings)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Update form settings");

        group.MapPut("/{id:guid}/slug", UpdateSlug)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Update form public link slug");

        group.MapPost("/{id:guid}/publish", PublishForm)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Publish form");

        group.MapPost("/{id:guid}/close", CloseForm)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Close form to new responses");

        group.MapPost("/{id:guid}/duplicate", DuplicateForm)
            .RequirePermission(Permissions.Forms.Manage)
            .WithSummary("Duplicate form");

        group.MapDelete("/{id:guid}", DeleteForm)
            .RequirePermission(Permissions.Forms.Manage)
            .RequireSudo()
            .WithSummary("Delete form and all responses");

        group.MapGet("/{id:guid}/responses", ListResponses)
            .RequirePermission(Permissions.Forms.View)
            .WithSummary("List form responses");

        group.MapGet("/{id:guid}/responses/export", ExportResponses)
            .RequirePermission(Permissions.Forms.View)
            .WithSummary("Export form responses as CSV");

        group.MapPost("/{id:guid}/responses/delete", DeleteResponses)
            .RequirePermission(Permissions.Forms.Manage)
            .RequireSudo()
            .WithSummary("Delete specific responses");

        var pub = endpoints.MapPublicGroup("forms", "Public forms");

        pub.MapGet("/{slug}", GetPublicForm)
            .WithSummary("Get live form definition");

        pub.MapPost("/{slug}/responses", SubmitPublicResponse)
            .WithSummary("Submit response to form");
    }

    private static FormSummaryResponse ToSummary(FormDefinition f, int responseCount) =>
        new(f.Id, f.Title, f.Slug, f.Status, responseCount, f.UpdatedAt ?? f.CreatedAt, f.PublishedAt);

    private static FormDetailResponse ToDetail(FormDefinition f, int responseCount)
    {
        JsonElement? fields = null;
        JsonElement? settings = null;
        try { fields = JsonDocument.Parse(f.FieldsJson).RootElement.Clone(); } catch { }
        try { settings = JsonDocument.Parse(f.SettingsJson).RootElement.Clone(); } catch { }
        return new FormDetailResponse(f.Id, f.Title, f.Slug, f.Description, f.Status, responseCount, f.UpdatedAt ?? f.CreatedAt, f.PublishedAt, fields, settings);
    }

    private static async Task<IResult> ListForms([AsParameters] FormQuery q, CommunicationsDbContext db, CancellationToken ct)
    {
        var query = db.Forms.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = $"%{q.Q.Trim()}%";
            query = query.Where(f => EF.Functions.ILike(f.Title, term) || EF.Functions.ILike(f.Slug, term));
        }

        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            query = query.Where(f => f.Status == q.Status.ToLowerInvariant());
        }

        var page = new PageRequest(q.Page, q.PageSize);
        var total = await query.LongCountAsync(ct);
        var forms = await query.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        var formIds = forms.Select(f => f.Id).ToList();
        var counts = await db.FormResponses.AsNoTracking()
            .Where(r => formIds.Contains(r.FormId))
            .GroupBy(r => r.FormId)
            .Select(g => new { FormId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FormId, x => x.Count, ct);

        var summaries = forms.Select(f => ToSummary(f, counts.GetValueOrDefault(f.Id, 0))).ToList();
        return Results.Ok(new PagedResult<FormSummaryResponse>(summaries, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> GetForm(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var count = await db.FormResponses.CountAsync(r => r.FormId == id, ct);
        return Results.Ok(ToDetail(f, count));
    }

    private static async Task<IResult> CreateForm(CreateFormInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            return Error.Validation("form.title_required", "Form title is required.",
                new Dictionary<string, string[]> { ["title"] = ["Form title is required."] }).ToError();
        }

        var slugBase = System.Text.RegularExpressions.Regex.Replace(req.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slugBase)) slugBase = "form";
        var slug = $"{slugBase}-{Guid.NewGuid():N}"[..Math.Min(30, slugBase.Length + 9)];

        var defaultSettings = JsonSerializer.Serialize(new
        {
            submitLabel = "Submit",
            confirmationTitle = "Thank you!",
            confirmationMessage = "Your response has been received.",
            redirectUrl = (string?)null,
            closesAt = (string?)null,
            responseLimit = (int?)null,
            notifyEmails = Array.Empty<string>(),
            audienceId = (string?)null,
        });

        var form = FormDefinition.Create(req.Title, slug, null, "[]", defaultSettings);
        db.Forms.Add(form);
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDetail(form, 0));
    }

    private static async Task<IResult> UpdateForm(Guid id, UpdateFormInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var fieldsJson = req.Fields.HasValue ? req.Fields.Value.GetRawText() : null;
        f.Update(req.Title, req.Description, fieldsJson);
        await db.SaveChangesAsync(ct);

        var count = await db.FormResponses.CountAsync(r => r.FormId == id, ct);
        return Results.Ok(ToDetail(f, count));
    }

    private static async Task<IResult> UpdateSettings(Guid id, JsonElement settings, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        f.UpdateSettings(settings.GetRawText());
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSlug(Guid id, UpdateSlugInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var clean = req.Slug.Trim().ToLowerInvariant();
        if (await db.Forms.AnyAsync(x => x.Id != id && x.Slug == clean, ct))
        {
            return Error.Conflict("form.slug_exists", "That link is already taken.",
                new Dictionary<string, string[]> { ["slug"] = ["That link is already taken."] }).ToError();
        }

        f.UpdateSlug(clean);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishForm(Guid id, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var now = clock.GetUtcNow();
        f.Publish(now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CloseForm(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        f.Close();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DuplicateForm(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var copy = FormDefinition.Create($"{f.Title} (copy)", $"{f.Slug}-copy-{Guid.NewGuid():N}"[..30], f.Description, f.FieldsJson, f.SettingsJson);
        db.Forms.Add(copy);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDetail(copy, 0));
    }

    private static async Task<IResult> DeleteForm(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return NotFound.ToError();

        var responses = await db.FormResponses.Where(r => r.FormId == id).ToListAsync(ct);
        db.FormResponses.RemoveRange(responses);
        db.Forms.Remove(f);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListResponses(Guid id, [AsParameters] FormQuery q, CommunicationsDbContext db, CancellationToken ct)
    {
        if (!await db.Forms.AnyAsync(f => f.Id == id, ct)) return NotFound.ToError();

        var query = db.FormResponses.AsNoTracking().Where(r => r.FormId == id);
        var page = new PageRequest(q.Page, q.PageSize);
        var total = await query.LongCountAsync(ct);
        var responses = await query.OrderByDescending(r => r.SubmittedAt).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        var items = responses.Select(r =>
        {
            JsonElement answers = default;
            try { answers = JsonDocument.Parse(r.AnswersJson).RootElement.Clone(); } catch { }
            return new FormResponseItem(r.Id, r.SubmittedAt, answers);
        }).ToList();

        return Results.Ok(new PagedResult<FormResponseItem>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> ExportResponses(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var form = await db.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (form is null) return NotFound.ToError();

        var responses = await db.FormResponses.AsNoTracking()
            .Where(r => r.FormId == id)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(10_000)
            .ToListAsync(ct);

        var rows = new List<IReadOnlyList<object?>> { new object?[] { "Submitted At", "Answers JSON" } };
        rows.AddRange(responses.Select(r => (IReadOnlyList<object?>)[r.SubmittedAt, r.AnswersJson]));

        return new CsvResult($"form-{form.Slug}-responses.csv", rows);
    }

    private static async Task<IResult> DeleteResponses(Guid id, DeleteResponsesInput req, CommunicationsDbContext db, CancellationToken ct)
    {
        var toDelete = await db.FormResponses.Where(r => r.FormId == id && req.ResponseIds.Contains(r.Id)).ToListAsync(ct);
        db.FormResponses.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deleted = toDelete.Count });
    }

    private static async Task<IResult> GetPublicForm(string slug, CommunicationsDbContext db, CancellationToken ct)
    {
        var f = await db.Forms.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug.ToLowerInvariant(), ct);
        if (f is null || f.Status == "draft") return NotFound.ToError();

        JsonElement? fields = null;
        JsonElement? settings = null;
        try { fields = JsonDocument.Parse(f.FieldsJson).RootElement.Clone(); } catch { }
        try { settings = JsonDocument.Parse(f.SettingsJson).RootElement.Clone(); } catch { }

        return Results.Ok(new PublicFormResponse(f.Slug, f.Title, f.Description, f.Status, fields, settings, "Eternal Sacred Order of Cherubim & Seraphim"));
    }

    private static async Task<IResult> SubmitPublicResponse(string slug, SubmitResponseInput req, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var f = await db.Forms.IgnoreQueryFilters([QueryFilters.Tenant]).FirstOrDefaultAsync(x => x.Slug == slug.ToLowerInvariant(), ct);
        if (f is null || f.Status != "published") return NotFound.ToError();

        var now = clock.GetUtcNow();
        var resp = FormResponseEntry.Create(f.Id, req.Answers.GetRawText(), now);
        resp.AssignTenant(f.TenantId);
        db.FormResponses.Add(resp);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}
