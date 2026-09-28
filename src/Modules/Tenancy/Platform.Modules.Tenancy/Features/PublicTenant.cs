using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Tenancy;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.Web.Endpoints;

namespace Platform.Modules.Tenancy.Features;

public sealed record PublicTenantResponse(
    Guid Id, string Slug, string Name, string? LogoUrl, string TimeZone, string DefaultCurrency, string DefaultLocale,
    string? ContactEmail, string? ContactPhone, string? WebsiteUrl, Address Address,
    IReadOnlyDictionary<string, JsonElement> Settings);

/// <summary>Bootstrap payload for the website and mobile app: branding, contact info and public settings.</summary>
public static class PublicTenant
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPublicGroup("tenant", "Public")
            .MapGet("/", async (ITenantContext tenant, TenancyDbContext db, HttpContext http, CancellationToken ct) =>
            {
                if (tenant.TenantId is not { } tenantId)
                {
                    return Results.Problem(title: "Organisation could not be resolved. Send the X-Tenant header.", statusCode: StatusCodes.Status404NotFound);
                }

                var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId, ct);
                var settings = await db.Settings.AsNoTracking().Where(s => s.IsPublic).ToListAsync(ct);

                http.Response.Headers.CacheControl = "public, max-age=60";
                return Results.Ok(new PublicTenantResponse(
                    t.Id, t.Slug, t.Name, t.LogoUrl, t.TimeZone, t.DefaultCurrency, t.DefaultLocale, t.ContactEmail,
                    t.ContactPhone, t.WebsiteUrl, t.Address,
                    settings.ToDictionary(s => s.Key, s => JsonDocument.Parse(s.Value).RootElement.Clone())));
            })
            .WithSummary("Public organisation profile for the website and mobile app");
}
