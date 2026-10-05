using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Security;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Tenancy.Features;

public sealed record SettingResponse(string Key, JsonElement Value, bool IsPublic, DateTimeOffset? UpdatedAt);

public sealed record SaveSettingRequest(JsonElement Value, bool IsPublic);

internal sealed class SaveSettingValidator : AbstractValidator<SaveSettingRequest>
{
    public const int MaxBytes = 64 * 1024;

    public SaveSettingValidator() =>
        RuleFor(x => x.Value).Must(v => v.GetRawText().Length <= MaxBytes).WithMessage("Setting value exceeds 64 KB.");
}

/// <summary>
/// Namespaced key/value settings, e.g. <c>branding.colors</c>, <c>social.links</c>,
/// <c>giving.online</c>. Public settings are served to the website and app.
/// </summary>
public static class Settings
{
    private const string KeyPattern = "^[a-z0-9]+(\\.[a-z0-9_-]+)*$";

    private static SettingResponse ToResponse(TenantSetting s) =>
        new(s.Key, JsonDocument.Parse(s.Value).RootElement.Clone(), s.IsPublic, s.UpdatedAt ?? s.CreatedAt);

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("settings", "Settings");

        group.MapGet("/", async (TenancyDbContext db, CancellationToken ct) =>
                TypedResults.Ok((await db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync(ct)).Select(ToResponse)))
            .RequirePermission(Permissions.Dashboard.View)
            .WithSummary("List all settings");

        group.MapPut("/{key}", async (string key, SaveSettingRequest request, TenancyDbContext db, CancellationToken ct) =>
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(key, KeyPattern) || key.Length > 128)
                {
                    return Results.Problem(title: "Invalid setting key.", statusCode: StatusCodes.Status400BadRequest);
                }

                var json = request.Value.GetRawText();
                var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
                if (setting is null)
                {
                    setting = TenantSetting.Create(key, json, request.IsPublic);
                    db.Settings.Add(setting);
                }
                else
                {
                    setting.Update(json, request.IsPublic);
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(ToResponse(setting));
            })
            .WithValidation<SaveSettingRequest>()
            .RequirePermission(Permissions.Settings.Manage)
            .WithSummary("Create or replace a setting");

        group.MapDelete("/{key}", async (string key, TenancyDbContext db, CancellationToken ct) =>
            {
                await db.Settings.Where(s => s.Key == key).ExecuteDeleteAsync(ct);
                return TypedResults.NoContent();
            })
            .RequirePermission(Permissions.Settings.Manage)
            .WithSummary("Delete a setting");
    }
}
