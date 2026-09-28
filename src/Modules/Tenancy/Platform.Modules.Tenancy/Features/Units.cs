using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Caching;
using Platform.Modules.Tenancy.Contracts;
using Platform.Modules.Tenancy.Domain;
using Platform.Modules.Tenancy.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Tenancy.Features;

public sealed record UnitResponse(
    Guid Id, string Slug, string Kind, string Name, Guid? ParentId, string? ParentName, int Depth, string Status, bool HoldsMembers,
    string? Tagline, IReadOnlyList<string> About, string? Locality, string? Address, string? Country, DateOnly? Established,
    ImageRef? Cover, ImageRef? Avatar, IReadOnlyList<UnitLeader> Leaders, IReadOnlyList<string> Phones, string? Email, int SortOrder,
    int ChildCount, DateTimeOffset UpdatedAt);

public sealed record SaveUnitRequest(
    string Name, string Kind, Guid? ParentId, string? Slug, string? Status, bool? HoldsMembers, string? Tagline, IReadOnlyList<string>? About,
    string? Locality, string? Address, string? Country, DateOnly? Established, ImageRef? Cover, ImageRef? Avatar,
    IReadOnlyList<UnitLeader>? Leaders, IReadOnlyList<string>? Phones, string? Email, int SortOrder = 0);

public sealed record MoveUnitRequest(Guid? ParentId);

public sealed record ParishResponse(Guid Id, string Name);

internal sealed class SaveUnitValidator : AbstractValidator<SaveUnitRequest>
{
    public SaveUnitValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a name.").MaximumLength(200);
        RuleFor(x => x.Kind).Must(k => UnitKinds.TryParse(k, out _)).WithMessage("Choose what kind of unit this is.");
        RuleFor(x => x.Slug).Must(s => s is null || Slug.IsValid(s)).WithMessage("Use lowercase letters, numbers and dashes.");
        RuleFor(x => x.Status).Must(s => s is null || Enum.TryParse<UnitStatus>(s, true, out _));
        RuleFor(x => x.Tagline).MaximumLength(300);
        RuleFor(x => x.About).Must(a => a is null || a.Count <= 30).WithMessage("Keep the description to 30 paragraphs or fewer.");
        RuleForEach(x => x.About).MaximumLength(4000);
        RuleFor(x => x.Locality).MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Country).Length(2).When(x => x.Country is not null).WithMessage("Use a 2-letter country code, e.g. NG.");
        RuleForEach(x => x.Phones).Matches(@"^\+\d{8,15}$").WithMessage("Use international format, e.g. +2348012345678.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256);
        RuleForEach(x => x.Leaders).ChildRules(l =>
        {
            l.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            l.RuleFor(x => x.Role).NotEmpty().MaximumLength(120);
        });
    }
}

/// <summary>The organisation's structure: Holy Order → headquarters → provinces → districts → branches (parishes)…</summary>
public static class Units
{
    private static readonly Error NotFound = Error.NotFound("unit.not_found", "We couldn’t find that part of the organisation.");
    private static readonly Error SlugTaken = Error.Conflict("unit.slug_taken", "Another unit already uses that web address.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var units = endpoints.MapModuleGroup("units", "Organisation");
        units.MapGet("/", List).WithSummary("Search units (flat list with parent names)");
        units.MapGet("/{id:guid}", Get).WithSummary("Get a unit");
        units.MapPost("/", Create).WithValidation<SaveUnitRequest>().RequirePermission(Permissions.Organisation.Manage).WithSummary("Add a unit");
        units.MapPut("/{id:guid}", Update).WithValidation<SaveUnitRequest>().RequirePermission(Permissions.Organisation.Manage).WithSummary("Update a unit");
        units.MapPost("/{id:guid}/move", Move).RequirePermission(Permissions.Organisation.Manage).WithSummary("Move a unit (and everything under it) to a new parent");
        units.MapDelete("/{id:guid}", Delete).RequirePermission(Permissions.Organisation.Manage).RequireSudo().WithSummary("Archive a unit with no sub-units");

        endpoints.MapModuleGroup("lookups", "Organisation")
            .MapGet("/parishes", Parishes).WithSummary("Parishes the caller may work with (scoped for parish-level admins)");

        endpoints.MapPublicGroup("parishes", "Public")
            .MapGet("/", PublicParishes).WithSummary("Parishes for the admin sign-up form");
    }

    private static UnitResponse ToResponse(Unit u, string? parentName, int childCount) => new(
        u.Id, u.Slug, UnitKinds.Format(u.Kind), u.Name, u.ParentId, parentName, u.Depth, u.Status.ToString().ToLowerInvariant(), u.HoldsMembers,
        u.Tagline, u.About, u.Locality, u.Address, u.Country, u.Established, u.Cover, u.Avatar, u.Leaders, u.Phones, u.Email, u.SortOrder,
        childCount, u.UpdatedAt ?? u.CreatedAt);

    private static async Task<IResult> List([AsParameters] PageRequest page, string? kind, Guid? parentId, TenancyDbContext db, CancellationToken ct)
    {
        var query = db.Units.AsNoTracking();
        if (page.Search is { } q)
        {
            query = query.Where(u => EF.Functions.ILike(u.Name, $"%{q}%") || EF.Functions.ILike(u.Locality ?? "", $"%{q}%"));
        }

        if (UnitKinds.TryParse(kind, out var k)) query = query.Where(u => u.Kind == k);
        if (parentId is { } p) query = query.Where(u => u.ParentId == p);

        query = page.Sort switch
        {
            "kind" => page.Descending ? query.OrderByDescending(u => u.Kind) : query.OrderBy(u => u.Kind),
            "updatedAt" => page.Descending ? query.OrderByDescending(u => u.UpdatedAt) : query.OrderBy(u => u.UpdatedAt),
            _ => page.Descending ? query.OrderByDescending(u => u.Name) : query.OrderBy(u => u.Depth).ThenBy(u => u.SortOrder).ThenBy(u => u.Name),
        };

        var total = await query.LongCountAsync(ct);
        var rows = await query.Skip(page.Skip).Take(page.SafePageSize)
            .Select(u => new
            {
                Unit = u,
                ParentName = db.Units.Where(p => p.Id == u.ParentId).Select(p => p.Name).FirstOrDefault(),
                Children = db.Units.Count(c => c.ParentId == u.Id),
            })
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<UnitResponse>(rows.Select(r => ToResponse(r.Unit, r.ParentName, r.Children)).ToList(),
            page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Get(Guid id, TenancyDbContext db, CancellationToken ct)
    {
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound.ToError();
        }

        var parentName = unit.ParentId is { } pid ? await db.Units.Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync(ct) : null;
        return Results.Ok(ToResponse(unit, parentName, await db.Units.CountAsync(c => c.ParentId == id, ct)));
    }

    private static async Task<IResult> Create(SaveUnitRequest r, TenancyDbContext db, ITenantContext tenant, IAuditLog audit, HybridCache cache, CancellationToken ct)
    {
        Unit? parent = null;
        if (r.ParentId is { } parentId && (parent = await db.Units.FirstOrDefaultAsync(u => u.Id == parentId, ct)) is null)
        {
            return Error.Validation("unit.invalid_parent", "The parent unit doesn’t exist.").ToError();
        }

        var slug = r.Slug ?? Slug.From(r.Name);
        if (await db.Units.AnyAsync(u => u.Slug == slug, ct))
        {
            return SlugTaken.ToError();
        }

        UnitKinds.TryParse(r.Kind, out var kind);
        var unit = Unit.Create(tenant.RequiredTenantId, slug, kind, r.Name, parent);
        unit.UpdateProfile(ToProfile(r, slug, kind, unit.HoldsMembers));
        db.Units.Add(unit);
        audit.Record("unit.created", $"Added {unit.Name}", target: new AuditTarget("unit", unit.Id.ToString(), unit.Name));
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.TenantTag(unit.TenantId), ct);
        return Results.Created($"/api/v1/units/{unit.Id}", ToResponse(unit, parent?.Name, 0));
    }

    private static async Task<IResult> Update(Guid id, SaveUnitRequest r, TenancyDbContext db, IAuditLog audit, HybridCache cache, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound.ToError();
        }

        var slug = r.Slug ?? unit.Slug;
        if (await db.Units.AnyAsync(u => u.Slug == slug && u.Id != id, ct))
        {
            return SlugTaken.ToError();
        }

        UnitKinds.TryParse(r.Kind, out var kind);
        var before = (unit.Name, unit.Slug, Kind: UnitKinds.Format(unit.Kind), unit.Status);
        unit.UpdateProfile(ToProfile(r, slug, kind, r.HoldsMembers ?? unit.HoldsMembers));
        audit.Record("unit.updated", $"Updated {unit.Name}", target: new AuditTarget("unit", unit.Id.ToString(), unit.Name),
            changes: AuditChanges.Diff(("name", before.Name, unit.Name), ("slug", before.Slug, unit.Slug),
                ("kind", before.Kind, UnitKinds.Format(unit.Kind)), ("status", before.Status.ToString(), unit.Status.ToString())));
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.TenantTag(unit.TenantId), ct);
        return await Get(id, db, ct);
    }

    private static async Task<IResult> Move(Guid id, MoveUnitRequest r, TenancyDbContext db, IAuditLog audit, HybridCache cache, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound.ToError();
        }

        Unit? parent = null;
        if (r.ParentId is { } parentId && (parent = await db.Units.FirstOrDefaultAsync(u => u.Id == parentId, ct)) is null)
        {
            return Error.Validation("unit.invalid_parent", "The new parent unit doesn’t exist.").ToError();
        }

        var oldPath = unit.Path;
        unit.PlaceUnder(parent);
        var descendants = await db.Units.Where(u => u.Path.StartsWith(oldPath) && u.Id != id).ToListAsync(ct);
        descendants.ForEach(d => unit.RebaseDescendant(d, oldPath));

        audit.Record("unit.moved", $"Moved {unit.Name} under {parent?.Name ?? "the top level"}", AuditSeverity.Warning,
            new AuditTarget("unit", unit.Id.ToString(), unit.Name));
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.TenantTag(unit.TenantId), ct);
        return await Get(id, db, ct);
    }

    private static async Task<IResult> Delete(Guid id, TenancyDbContext db, IAuditLog audit, HybridCache cache, CancellationToken ct)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null)
        {
            return NotFound.ToError();
        }

        if (await db.Units.AnyAsync(u => u.ParentId == id, ct))
        {
            return Error.Conflict("unit.has_children", "Move or archive the units under it first.").ToError();
        }

        db.Units.Remove(unit);
        audit.Record("unit.deleted", $"Archived {unit.Name}", AuditSeverity.Warning, new AuditTarget("unit", unit.Id.ToString(), unit.Name));
        await db.SaveChangesAsync(ct);
        await cache.RemoveByTagAsync(CacheKeys.TenantTag(unit.TenantId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Parishes(IUnitDirectory units, ICurrentAccess access, CancellationToken ct)
    {
        var scope = (await access.GetAsync(ct))?.ScopeUnitId;
        return Results.Ok((await units.GetParishesAsync(scope, ct)).Select(p => new ParishResponse(p.Id, p.Name)));
    }

    private static async Task<IResult> PublicParishes(ITenantContext tenant, IUnitDirectory units, HttpContext http, CancellationToken ct)
    {
        if (tenant.TenantId is null)
        {
            return Results.Ok(Array.Empty<ParishResponse>());
        }

        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok((await units.GetParishesAsync(null, ct)).Select(p => new ParishResponse(p.Id, p.Name)));
    }

    private static UnitProfile ToProfile(SaveUnitRequest r, string slug, UnitKind kind, bool holdsMembers) => new(
        slug, kind, r.Name, Enum.TryParse<UnitStatus>(r.Status, true, out var s) ? s : UnitStatus.Active, holdsMembers, r.Tagline, r.About,
        r.Locality, r.Address, r.Country, r.Established, r.Cover, r.Avatar, r.Leaders, r.Phones, r.Email, r.SortOrder);
}
