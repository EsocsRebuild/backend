using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Communications.Features;

public sealed record AudienceListResponse(
    Guid Id,
    string Name,
    string? Description,
    int SubscriberCount,
    int UnsubscribedCount,
    bool DoubleOptIn,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ContactResponse(
    Guid Id,
    string Email,
    string? FirstName,
    string? LastName,
    string Status,
    string Source,
    Guid? MemberId,
    DateTimeOffset? SubscribedAt,
    DateTimeOffset CreatedAt);

public sealed record CreateAudienceRequest(string Name, string? Description, bool DoubleOptIn = false);
public sealed record UpdateAudienceRequest(string? Name, string? Description, bool? DoubleOptIn);

public sealed record AddContactRequest(string Email, string? FirstName, string? LastName, string? Status);
public sealed record ImportContactItem(string Email, string? FirstName, string? LastName);
public sealed record ImportContactsRequest(IReadOnlyList<ImportContactItem> Contacts, bool Consent = true, bool UpdateExisting = false);
public sealed record SyncMembersRequest(Guid? ParishId);
public sealed record RemoveContactsRequest(IReadOnlyList<Guid> Ids);
public sealed record EstimateAudienceRequest(IReadOnlyList<Guid> ListIds);
public sealed record ImportResultResponse(int Created, int Updated, int Skipped, int Invalid);

public sealed record ContactQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Sort = null, string? Dir = null, string? Status = null);

public static class AudienceEndpoints
{
    private static readonly Error NotFound = Error.NotFound("audiences.not_found", "We couldn't find that audience.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("audiences", "Audiences");

        group.MapGet("/", ListAudiences)
            .RequirePermission(Permissions.Audiences.View)
            .WithSummary("List all audience lists");

        group.MapGet("/{id:guid}", GetAudience)
            .RequirePermission(Permissions.Audiences.View)
            .WithSummary("Get audience list by ID");

        group.MapPost("/", CreateAudience)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Create audience list");

        group.MapPatch("/{id:guid}", UpdateAudience)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Update audience list");

        group.MapDelete("/{id:guid}", DeleteAudience)
            .RequirePermission(Permissions.Audiences.Manage)
            .RequireSudo()
            .WithSummary("Delete audience list");

        group.MapGet("/{id:guid}/contacts", ListContacts)
            .RequirePermission(Permissions.Audiences.View)
            .WithSummary("List contacts in an audience");

        group.MapPost("/{id:guid}/contacts", AddContact)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Add a contact to an audience");

        group.MapPost("/{id:guid}/imports", ImportContacts)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Import a batch of contacts");

        group.MapPost("/{id:guid}/sync-members", SyncMembers)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Sync consenting members into audience");

        group.MapPost("/{id:guid}/contacts/remove", RemoveContacts)
            .RequirePermission(Permissions.Audiences.Manage)
            .WithSummary("Remove contacts from audience");

        group.MapPost("/estimate", Estimate)
            .RequirePermission(Permissions.Campaigns.Manage)
            .WithSummary("Estimate total reachable recipients across lists");
    }

    private static async Task<AudienceListResponse> ToResponseAsync(AudienceList a, CommunicationsDbContext db, CancellationToken ct)
    {
        var subCount = await db.Contacts.CountAsync(c => c.ListId == a.Id && c.Status == "subscribed", ct);
        var unsubCount = await db.Contacts.CountAsync(c => c.ListId == a.Id && c.Status == "unsubscribed", ct);
        return new AudienceListResponse(a.Id, a.Name, a.Description, subCount, unsubCount, a.DoubleOptIn, a.CreatedAt, a.UpdatedAt ?? a.CreatedAt);
    }

    private static async Task<IResult> ListAudiences(CommunicationsDbContext db, CancellationToken ct)
    {
        var lists = await db.Audiences.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var listIds = lists.Select(a => a.Id).ToList();

        var subCounts = await db.Contacts.AsNoTracking()
            .Where(c => listIds.Contains(c.ListId) && c.Status == "subscribed")
            .GroupBy(c => c.ListId)
            .Select(g => new { ListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListId, x => x.Count, ct);

        var unsubCounts = await db.Contacts.AsNoTracking()
            .Where(c => listIds.Contains(c.ListId) && c.Status == "unsubscribed")
            .GroupBy(c => c.ListId)
            .Select(g => new { ListId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ListId, x => x.Count, ct);

        var results = lists.Select(a => new AudienceListResponse(
            a.Id, a.Name, a.Description,
            subCounts.GetValueOrDefault(a.Id, 0),
            unsubCounts.GetValueOrDefault(a.Id, 0),
            a.DoubleOptIn, a.CreatedAt, a.UpdatedAt ?? a.CreatedAt)).ToList();

        return Results.Ok(results);
    }

    private static async Task<IResult> GetAudience(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var a = await db.Audiences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound.ToError();
        return Results.Ok(await ToResponseAsync(a, db, ct));
    }

    private static async Task<IResult> CreateAudience(CreateAudienceRequest req, CommunicationsDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            return Error.Validation("audience.name_required", "Audience name is required.",
                new Dictionary<string, string[]> { ["name"] = ["Audience name is required."] }).ToError();
        }

        var audience = AudienceList.Create(req.Name, req.Description, req.DoubleOptIn);
        db.Audiences.Add(audience);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(audience, db, ct));
    }

    private static async Task<IResult> UpdateAudience(Guid id, UpdateAudienceRequest req, CommunicationsDbContext db, CancellationToken ct)
    {
        var a = await db.Audiences.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound.ToError();

        a.Update(req.Name ?? a.Name, req.Description ?? a.Description, req.DoubleOptIn ?? a.DoubleOptIn);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(a, db, ct));
    }

    private static async Task<IResult> DeleteAudience(Guid id, CommunicationsDbContext db, CancellationToken ct)
    {
        var a = await db.Audiences.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound.ToError();

        var contacts = await db.Contacts.Where(c => c.ListId == id).ToListAsync(ct);
        db.Contacts.RemoveRange(contacts);
        db.Audiences.Remove(a);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListContacts(Guid id, [AsParameters] ContactQuery q, CommunicationsDbContext db, CancellationToken ct)
    {
        if (!await db.Audiences.AnyAsync(a => a.Id == id, ct)) return NotFound.ToError();

        var query = db.Contacts.AsNoTracking().Where(c => c.ListId == id);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = $"%{q.Q.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Email, term) ||
                                     EF.Functions.ILike(c.FirstName ?? "", term) ||
                                     EF.Functions.ILike(c.LastName ?? "", term));
        }

        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            query = query.Where(c => c.Status == q.Status.ToLowerInvariant());
        }

        query = (q.Sort, q.Dir?.ToLowerInvariant()) switch
        {
            ("email", "asc") => query.OrderBy(c => c.Email),
            ("email", "desc") => query.OrderByDescending(c => c.Email),
            ("firstName", "asc") => query.OrderBy(c => c.FirstName),
            ("firstName", "desc") => query.OrderByDescending(c => c.FirstName),
            ("lastName", "asc") => query.OrderBy(c => c.LastName),
            ("lastName", "desc") => query.OrderByDescending(c => c.LastName),
            _ => query.OrderByDescending(c => c.CreatedAt),
        };

        var page = new PageRequest(q.Page, q.PageSize);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        var responses = items.Select(c => new ContactResponse(
            c.Id, c.Email, c.FirstName, c.LastName, c.Status, c.Source, c.MemberId, c.SubscribedAt, c.CreatedAt)).ToList();

        return Results.Ok(new PagedResult<ContactResponse>(responses, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> AddContact(Guid id, AddContactRequest req, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Audiences.AnyAsync(a => a.Id == id, ct)) return NotFound.ToError();
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
        {
            return Error.Validation("contact.invalid_email", "Valid email is required.",
                new Dictionary<string, string[]> { ["email"] = ["Valid email is required."] }).ToError();
        }

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var existing = await db.Contacts.FirstOrDefaultAsync(c => c.ListId == id && c.Email == normalizedEmail, ct);
        var now = clock.GetUtcNow();

        if (existing is null)
        {
            var contact = AudienceContact.Create(id, normalizedEmail, req.FirstName, req.LastName, req.Status ?? "subscribed", "manual", null, now);
            db.Contacts.Add(contact);
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ImportContacts(Guid id, ImportContactsRequest req, CommunicationsDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Audiences.AnyAsync(a => a.Id == id, ct)) return NotFound.ToError();

        var existingEmails = (await db.Contacts.Where(c => c.ListId == id).Select(c => c.Email).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int created = 0, skipped = 0, invalid = 0;
        var now = clock.GetUtcNow();

        foreach (var c in req.Contacts)
        {
            if (string.IsNullOrWhiteSpace(c.Email) || !c.Email.Contains('@'))
            {
                invalid++;
                continue;
            }

            var email = c.Email.Trim().ToLowerInvariant();
            if (existingEmails.Contains(email))
            {
                skipped++;
                continue;
            }

            var contact = AudienceContact.Create(id, email, c.FirstName, c.LastName, "subscribed", "import", null, now);
            db.Contacts.Add(contact);
            existingEmails.Add(email);
            created++;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportResultResponse(created, 0, skipped, invalid));
    }

    private static async Task<IResult> SyncMembers(Guid id, SyncMembersRequest req, CommunicationsDbContext db, IPeopleDirectory people, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Audiences.AnyAsync(a => a.Id == id, ct)) return NotFound.ToError();

        var filter = new AudienceFilter(UnitId: req.ParishId, RequireConsent: true);
        var members = await people.FindContactsAsync(filter, ct);

        var existingEmails = (await db.Contacts.Where(c => c.ListId == id).Select(c => c.Email).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int created = 0, skipped = 0;
        var now = clock.GetUtcNow();

        foreach (var m in members)
        {
            if (string.IsNullOrWhiteSpace(m.Email) || !m.Email.Contains('@')) continue;

            var email = m.Email.Trim().ToLowerInvariant();
            if (existingEmails.Contains(email))
            {
                skipped++;
                continue;
            }

            var names = m.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var first = names.ElementAtOrDefault(0) ?? m.FirstName;
            var last = names.ElementAtOrDefault(1);

            var contact = AudienceContact.Create(id, email, first, last, "subscribed", "member", m.PersonId, now);
            db.Contacts.Add(contact);
            existingEmails.Add(email);
            created++;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportResultResponse(created, 0, skipped, 0));
    }

    private static async Task<IResult> RemoveContacts(Guid id, RemoveContactsRequest req, CommunicationsDbContext db, CancellationToken ct)
    {
        var toRemove = await db.Contacts.Where(c => c.ListId == id && req.Ids.Contains(c.Id)).ToListAsync(ct);
        db.Contacts.RemoveRange(toRemove);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { removed = toRemove.Count });
    }

    private static async Task<IResult> Estimate(EstimateAudienceRequest req, CommunicationsDbContext db, CancellationToken ct)
    {
        if (req.ListIds == null || req.ListIds.Count == 0) return Results.Ok(new { count = 0 });

        var count = await db.Contacts.AsNoTracking()
            .Where(c => req.ListIds.Contains(c.ListId) && c.Status == "subscribed")
            .Select(c => c.Email)
            .Distinct()
            .CountAsync(ct);

        return Results.Ok(new { count });
    }
}
