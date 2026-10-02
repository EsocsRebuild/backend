using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Tenancy.Features;

public sealed record TenantResponse(
    Guid Id, string Slug, string Name, string? LegalName, string Status, string Kind, string? PlanCode,
    string TimeZone, string DefaultCurrency, string DefaultLocale, string? ContactEmail, string? ContactPhone,
    string? WebsiteUrl, string? LogoUrl, Address Address, IReadOnlyList<TenantDomainResponse> Domains, DateTimeOffset CreatedAt);

public sealed record TenantDomainResponse(Guid Id, string Host, bool IsPrimary, bool IsVerified, string VerificationToken);

internal static class TenantMapping
{
    public static TenantResponse ToResponse(this Tenant t) => new(
        t.Id, t.Slug, t.Name, t.LegalName, t.Status.ToString(), t.Kind, t.PlanCode, t.TimeZone, t.DefaultCurrency,
        t.DefaultLocale, t.ContactEmail, t.ContactPhone, t.WebsiteUrl, t.LogoUrl, t.Address,
        t.Domains.Select(d => new TenantDomainResponse(d.Id, d.Host, d.IsPrimary, d.VerifiedAt is not null, d.VerificationToken)).ToList(),
        t.CreatedAt);
}

internal static class TenantErrors
{
    public static readonly Error NotFound = Error.NotFound("tenant.not_found", "The organisation was not found.");
    public static readonly Error SlugTaken = Error.Conflict("tenant.slug_taken", "That organisation handle is already taken.");
    public static readonly Error DomainTaken = Error.Conflict("tenant.domain_taken", "That domain is already in use.");
}

public static class GetCurrentTenant
{
    public sealed record Query;

    internal sealed class Handler(TenancyDbContext db, ITenantContext tenant) : IQueryHandler<Query, TenantResponse>
    {
        public async Task<Result<TenantResponse>> Handle(Query query, CancellationToken ct)
        {
            var entity = await db.Tenants.AsNoTracking().Include(t => t.Domains)
                .FirstOrDefaultAsync(t => t.Id == tenant.RequiredTenantId, ct);
            return entity is null ? TenantErrors.NotFound : entity.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/", async (IQueryHandler<Query, TenantResponse> handler, CancellationToken ct) =>
                (await handler.Handle(new Query(), ct)).ToHttp())
            .RequirePermission(Permissions.Dashboard.View)
            .WithName("GetCurrentTenant")
            .WithSummary("Get the current organisation profile");
}

public static class UpdateCurrentTenant
{
    public sealed record Command(
        string Name, string? LegalName, string TimeZone, string DefaultCurrency, string DefaultLocale,
        string? ContactEmail, string? ContactPhone, string? WebsiteUrl, string? LogoUrl, Address? Address);

    internal sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.LegalName).MaximumLength(200);
            RuleFor(x => x.TimeZone).NotEmpty().Must(BeValidTimeZone).WithMessage("Unknown IANA time zone.");
            RuleFor(x => x.DefaultCurrency).NotEmpty().Length(3);
            RuleFor(x => x.DefaultLocale).NotEmpty().MaximumLength(16);
            RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256);
            RuleFor(x => x.ContactPhone).MaximumLength(32);
            RuleFor(x => x.WebsiteUrl).MaximumLength(512);
            RuleFor(x => x.LogoUrl).MaximumLength(1024);
        }

        private static bool BeValidTimeZone(string tz) => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _);
    }

    internal sealed class Handler(TenancyDbContext db, ITenantContext tenant, Microsoft.Extensions.Caching.Hybrid.HybridCache cache) : ICommandHandler<Command, TenantResponse>
    {
        public async Task<Result<TenantResponse>> Handle(Command c, CancellationToken ct)
        {
            var entity = await db.Tenants.Include(t => t.Domains).FirstOrDefaultAsync(t => t.Id == tenant.RequiredTenantId, ct);
            if (entity is null)
            {
                return TenantErrors.NotFound;
            }

            entity.UpdateProfile(c.Name, c.LegalName, c.TimeZone, c.DefaultCurrency, c.DefaultLocale,
                c.ContactEmail, c.ContactPhone, c.WebsiteUrl, c.LogoUrl, c.Address ?? Address.Empty);
            await db.SaveChangesAsync(ct);
            await cache.RemoveByTagAsync(Platform.Infrastructure.Caching.CacheKeys.TenantTag(entity.Id), ct);
            return entity.ToResponse();
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPut("/", async (Command command, ICommandHandler<Command, TenantResponse> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp())
            .WithValidation<Command>()
            .RequirePermission(Permissions.Organisation.Manage)
            .WithName("UpdateCurrentTenant")
            .WithSummary("Update the current organisation profile");
}

public static class ManageTenantDomains
{
    public sealed record AddCommand(string Host, bool IsPrimary);

    internal sealed class Validator : AbstractValidator<AddCommand>
    {
        public Validator() =>
            RuleFor(x => x.Host).NotEmpty().MaximumLength(253)
                .Matches(@"^(?=.{1,253}$)([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$")
                .WithMessage("Must be a valid host name, e.g. www.example.org.");
    }

    internal sealed class AddHandler(TenancyDbContext db, ITenantContext tenant) : ICommandHandler<AddCommand, TenantDomainResponse>
    {
        public async Task<Result<TenantDomainResponse>> Handle(AddCommand c, CancellationToken ct)
        {
            var host = c.Host.Trim().ToLowerInvariant();
            if (await db.TenantDomains.AnyAsync(d => d.Host == host, ct))
            {
                return TenantErrors.DomainTaken;
            }

            var entity = await db.Tenants.Include(t => t.Domains).FirstAsync(t => t.Id == tenant.RequiredTenantId, ct);
            var domain = entity.AddDomain(host, c.IsPrimary);
            await db.SaveChangesAsync(ct);
            return new TenantDomainResponse(domain.Id, domain.Host, domain.IsPrimary, false, domain.VerificationToken);
        }
    }

    public static void Map(IEndpointRouteBuilder group)
    {
        group.MapPost("/domains", async (AddCommand command, ICommandHandler<AddCommand, TenantDomainResponse> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct)).ToHttp())
            .WithValidation<AddCommand>()
            .RequirePermission(Permissions.Organisation.Manage)
            .WithSummary("Register a custom domain for the public website");

        group.MapDelete("/domains/{domainId:guid}", async (Guid domainId, TenancyDbContext db, ITenantContext tenant, CancellationToken ct) =>
            {
                var entity = await db.Tenants.Include(t => t.Domains).FirstAsync(t => t.Id == tenant.RequiredTenantId, ct);
                entity.RemoveDomain(domainId);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .RequirePermission(Permissions.Organisation.Manage)
            .WithSummary("Remove a custom domain");
    }
}
