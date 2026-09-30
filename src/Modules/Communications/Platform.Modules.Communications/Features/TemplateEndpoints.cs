using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record TemplateUserRef(Guid Id, string Name);

public sealed record TemplateResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt,
    TemplateUserRef? UpdatedBy,
    JsonElement? Content);

public sealed record SaveTemplateInput(string Name, string? Description, JsonElement? Content);

public static class TemplateEndpoints
{
    private static readonly Error NotFound = Error.NotFound("templates.not_found", "We couldn't find that template.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("templates", "Templates");

        // Allowed for both templates:manage and campaigns:manage (the composer's picker)
        group.MapGet("/", ListTemplates)
            .RequireAuthorization()
            .WithSummary("List all email templates");

        group.MapGet("/{id:guid}", GetTemplate)
            .RequirePermission(Permissions.Templates.Manage)
            .WithSummary("Get email template by ID");

        group.MapPost("/", CreateTemplate)
            .RequirePermission(Permissions.Templates.Manage)
            .WithSummary("Create email template");

        group.MapPatch("/{id:guid}", UpdateTemplate)
            .RequirePermission(Permissions.Templates.Manage)
            .WithSummary("Update email template");

        group.MapPost("/{id:guid}/duplicate", DuplicateTemplate)
            .RequirePermission(Permissions.Templates.Manage)
            .WithSummary("Duplicate email template");

        group.MapDelete("/{id:guid}", DeleteTemplate)
            .RequirePermission(Permissions.Templates.Manage)
            .WithSummary("Delete email template");
    }

    private static TemplateResponse ToResponse(EmailTemplate t)
    {
        JsonElement? content = null;
        if (!string.IsNullOrWhiteSpace(t.ContentJson))
        {
            try { content = JsonDocument.Parse(t.ContentJson).RootElement.Clone(); }
            catch { content = null; }
        }

        var updatedBy = t.UpdatedById.HasValue && !string.IsNullOrWhiteSpace(t.UpdatedByName)
            ? new TemplateUserRef(t.UpdatedById.Value, t.UpdatedByName)
            : null;

        return new TemplateResponse(t.Id, t.Name, t.Description, t.UpdatedAt ?? t.CreatedAt, updatedBy, content);
    }

    private static async Task<IResult> ListTemplates(CommunicationsDbContext db, ICurrentAccess access, CancellationToken ct)
    {
        var a = await access.GetAsync(ct);
        if (a == null || (!a.Can(Permissions.Templates.Manage) && !a.Can(Permissions.Campaigns.Manage) && !a.Can(Permissions.Campaigns.View)))
        {
            return Error.Forbidden("templates.forbidden", "You don't have permission to view templates.").ToError();
        }

        var templates = await db.EmailTemplates.AsNoTracking().OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).ToListAsync(ct);
        return Results.Ok(templates.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetTemplate(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var t = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound.ToError();
        return Results.Ok(ToResponse(t));
    }

    private static async Task<IResult> CreateTemplate(SaveTemplateInput req, CommunicationsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return Error.Validation("template.name_required", "Template name is required.",
                new Dictionary<string, string[]> { ["name"] = ["Template name is required."] }).ToError();
        }

        var contentJson = req.Content.HasValue ? req.Content.Value.GetRawText() : "{}";
        var template = EmailTemplate.Create(req.Name, req.Description, contentJson, user.UserId, user.UserId?.ToString());
        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(template));
    }

    private static async Task<IResult> UpdateTemplate(Guid id, SaveTemplateInput req, CommunicationsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound.ToError();

        var contentJson = req.Content.HasValue ? req.Content.Value.GetRawText() : t.ContentJson;
        t.Update(req.Name ?? t.Name, req.Description ?? t.Description, contentJson, user.UserId, user.UserId?.ToString());
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(t));
    }

    private static async Task<IResult> DuplicateTemplate(Guid id, CommunicationsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var t = await db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound.ToError();

        var copy = EmailTemplate.Create($"{t.Name} (copy)", t.Description, t.ContentJson, user.UserId, user.UserId?.ToString());
        db.EmailTemplates.Add(copy);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(copy));
    }

    private static async Task<IResult> DeleteTemplate(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var t = await db.EmailTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound.ToError();

        db.EmailTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
