using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Messaging;
using Platform.Application.Pagination;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Caching;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.Tenancy.Features;

/// <summary>
/// SaaS operator endpoints (platform administrators only): onboard and manage organisations.
/// </summary>
public static class PlatformTenants
{
    public const string PlatformAdminPolicy = "PlatformAdmin";

    public sealed record CreateCommand(
        string Slug, string Name, string OwnerEmail, string OwnerFirstName, string OwnerLastName,
        string Kind = "church", string TimeZone = "UTC", string Currency = "USD", string Locale = "en");

    internal sealed class Validator : AbstractValidator<CreateCommand>
    {
        public Validator()
        {
            RuleFor(x => x.Slug).NotEmpty().Length(3, 63).Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")
                .WithMessage("Use 3–63 lowercase letters, digits and hyphens.");
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.OwnerFirstName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.OwnerLastName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Kind).NotEmpty().MaximumLength(32);
            RuleFor(x => x.TimeZone).Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("Unknown IANA time zone.");
            RuleFor(x => x.Currency).Length(3);
            RuleFor(x => x.Locale).NotEmpty().MaximumLength(16);
        }
    }

    /// <summary>Creates the tenant and its headquarters branch; Identity provisions roles and the owner via event.</summary>
    public sealed class CreateHandler(TenancyDbContext db, HybridCache cache, ITenantContextSetter tenantContext) : ICommandHandler<CreateCommand, TenantResponse>
    {
        public async Task<Result<TenantResponse>> Handle(CreateCommand c, CancellationToken ct)
        {
            var slug = c.Slug.ToLowerInvariant();
            if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            {
                return TenantErrors.SlugTaken;
            }

            var tenant = Tenant.Create(slug, c.Name, c.Kind, c.TimeZone, c.Currency, c.Locale, c.OwnerEmail, c.OwnerFirstName, c.OwnerLastName);
            db.Tenants.Add(tenant);

            // Onboarding acts inside the new organisation (the operator's own tenant must not leak in).
            tenantContext.SetTenant(tenant.Id);

            var headquarters = Branch.Create("Headquarters", "HQ", isHeadquarters: true);
            headquarters.AssignTenant(tenant.Id);
            db.Branches.Add(headquarters);

            await db.SaveChangesAsync(ct);
            await cache.RemoveAsync(CacheKeys.TenantBySlug(slug), ct);
            return tenant.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/platform/tenants")
            .WithTags("Platform")
            .RequireAuthorization(PlatformAdminPolicy);

        group.MapGet("/", async ([AsParameters] PageRequest page, string? search, TenancyDbContext db, CancellationToken ct) =>
            {
                var query = db.Tenants.AsNoTracking();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(t => EF.Functions.ILike(t.Name, $"%{search}%") || EF.Functions.ILike(t.Slug, $"%{search}%"));
                }

                var total = await query.LongCountAsync(ct);
                var items = await query.OrderBy(t => t.Name).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
                return TypedResults.Ok(new PagedResult<TenantResponse>(items.Select(t => t.ToResponse()).ToList(), page.SafePage, page.SafePageSize, total));
            })
            .WithSummary("List organisations");

        group.MapPost("/", async (CreateCommand command, ICommandHandler<CreateCommand, TenantResponse> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToCreated(t => $"/api/v1/platform/tenants/{t.Id}"))
            .WithValidation<CreateCommand>()
            .WithSummary("Onboard a new organisation");

        group.MapPost("/{id:guid}/status/{status}", async (Guid id, TenantStatus status, TenancyDbContext db, HybridCache cache, CancellationToken ct) =>
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
                if (tenant is null)
                {
                    return TenantErrors.NotFound.ToProblem();
                }

                tenant.ChangeStatus(status);
                await db.SaveChangesAsync(ct);
                await cache.RemoveAsync(CacheKeys.TenantBySlug(tenant.Slug), ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Activate, suspend or cancel an organisation");
    }
}
