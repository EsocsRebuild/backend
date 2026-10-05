using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.People.Features;

public sealed record ParishRef(Guid Id, string Name);

public sealed record CreatorRef(Guid Id, string Name);

/// <summary>Contract <c>MemberSummary</c> (admin portal <c>features/members/types.ts</c>).</summary>
public sealed record MemberSummaryResponse(
    Guid Id, string MemberNumber, string FirstName, string LastName, string? Email, string? Phone, string? AvatarUrl, ParishRef? Parish,
    string? Rank, string Status, bool EmailConsent, DateTimeOffset JoinedAt);

/// <summary>Contract <c>Member</c>, plus the church-journey fields the portal may show later.</summary>
public sealed record MemberResponse(
    Guid Id, string MemberNumber, string FirstName, string LastName, string? Email, string? Phone, string? AvatarUrl, ParishRef? Parish,
    string? Rank, string Status, bool EmailConsent, DateTimeOffset JoinedAt, string? Gender, DateOnly? DateOfBirth, string? Address,
    string? Notes, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, CreatorRef? CreatedBy,
    string Stage, IReadOnlyList<string> Tags, Guid? HouseholdId, bool HasAccount);

public sealed record MemberInput(
    string FirstName, string LastName, string? Email, string? Phone, Guid ParishId, string? Rank, string? Gender, DateOnly? DateOfBirth,
    string? Address, string? Notes, string Status = "active", bool EmailConsent = false);

public sealed record BulkMembersRequest(IReadOnlyList<Guid> Ids, string Action);

public sealed record BulkDeleteRequest(IReadOnlyList<Guid> Ids);

public sealed record MemberQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Sort = null, string? Dir = null, string? Status = null, Guid? ParishId = null);

public sealed record StatusChangeResponse(Guid Id, string From, string To, DateOnly EffectiveDate, string? Reason, DateTimeOffset RecordedAt, Guid? RecordedBy);

public sealed record ChangeStageRequest(MembershipStatus Stage, DateOnly? EffectiveDate, string? Reason);

internal sealed class MemberInputValidator : AbstractValidator<MemberInput>
{
    public MemberInputValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("Enter a first name.").MaximumLength(60);
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Enter a last name.").MaximumLength(60);
        RuleFor(x => x.Email).EmailAddress().WithMessage("That email doesn’t look right.").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).Matches(@"^[+\d][\d\s()-]{6,24}$").WithMessage("Enter a valid phone number.").When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.ParishId).NotEmpty().WithMessage("Choose a parish.");
        RuleFor(x => x.Rank).MaximumLength(60).WithMessage("Keep this under 60 characters.");
        RuleFor(x => x.Gender).Must(g => g is null or "" or "female" or "male").WithMessage("Choose female or male.");
        RuleFor(x => x.DateOfBirth).LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue).WithMessage("Enter a valid date.");
        RuleFor(x => x.Address).MaximumLength(240).WithMessage("Keep this under 240 characters.");
        RuleFor(x => x.Notes).MaximumLength(2000).WithMessage("Keep this under 2000 characters.");
        RuleFor(x => x.Status).Must(s => Enum.TryParse<RecordStatus>(s, true, out _)).WithMessage("Choose a status.");
    }
}

internal sealed class BulkMembersValidator : AbstractValidator<BulkMembersRequest>
{
    public BulkMembersValidator()
    {
        RuleFor(x => x.Ids).NotEmpty().Must(i => i.Count <= 500).WithMessage("Select 500 or fewer at a time.");
        RuleFor(x => x.Action).Must(a => a is "approve" or "deactivate");
    }
}

internal sealed class BulkDeleteValidator : AbstractValidator<BulkDeleteRequest>
{
    public BulkDeleteValidator() => RuleFor(x => x.Ids).NotEmpty().Must(i => i.Count <= 500).WithMessage("Select 500 or fewer at a time.");
}

/// <summary>
/// Limits member queries to the caller's parish (and everything under it) for parish-level administrators.
/// Organisation-wide administrators see everyone.
/// </summary>
internal sealed class MemberScope(ICurrentAccess access, IUnitDirectory units)
{
    private IReadOnlyList<Guid>? _allowed;
    private bool _loaded;

    public async Task<IReadOnlyList<Guid>?> AllowedUnitsAsync(CancellationToken ct)
    {
        if (!_loaded)
        {
            var scope = (await access.GetAsync(ct))?.ScopeUnitId;
            _allowed = scope is { } unitId ? await units.GetSubtreeIdsAsync(unitId, ct) : null;
            _loaded = true;
        }

        return _allowed;
    }

    public async Task<IQueryable<Person>> ApplyAsync(IQueryable<Person> query, CancellationToken ct) =>
        await AllowedUnitsAsync(ct) is { } allowed ? query.Where(p => p.UnitId != null && allowed.Contains(p.UnitId.Value)) : query;

    public async Task<bool> CanUseUnitAsync(Guid unitId, CancellationToken ct) =>
        await AllowedUnitsAsync(ct) is not { } allowed || allowed.Contains(unitId);
}

/// <summary>The member register (API contract §7), scoped by parish for parish-level administrators.</summary>
public static class MembersEndpoints
{
    internal static readonly Error NotFound = Error.NotFound("member.not_found", "We couldn’t find that member.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("members", "Members");
        group.MapGet("/", List).RequirePermission(Permissions.Members.View).WithSummary("Search members");
        group.MapGet("/keyset", KeysetList).RequirePermission(Permissions.Members.View).WithSummary("Keyset paginated members");
        group.MapGet("/export", Export).RequirePermission(Permissions.Members.Export).WithSummary("Download members as CSV");
        group.MapGet("/stats", Stats).RequirePermission(Permissions.Members.View).WithSummary("Membership figures and upcoming celebrations");
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.Members.View).WithSummary("A member");
        group.MapPost("/", Create).WithValidation<MemberInput>().RequirePermission(Permissions.Members.Manage).WithSummary("Add a member");
        group.MapPatch("/{id:guid}", Update).WithValidation<MemberInput>().RequirePermission(Permissions.Members.Manage).WithSummary("Update a member");
        group.MapPost("/bulk", Bulk).WithValidation<BulkMembersRequest>().RequirePermission(Permissions.Members.Manage).WithSummary("Approve or deactivate many members");
        group.MapDelete("/{id:guid}", Delete).RequirePermission(Permissions.Members.Manage).RequireSudo().WithSummary("Delete a member");
        group.MapPost("/bulk-delete", BulkDelete).WithValidation<BulkDeleteRequest>().RequirePermission(Permissions.Members.Manage).RequireSudo()
            .WithSummary("Delete many members");
        group.MapPost("/{id:guid}/stage", ChangeStage).RequirePermission(Permissions.Members.Manage).WithSummary("Move a person along their church journey");
        group.MapGet("/{id:guid}/stage-history", StageHistory).RequirePermission(Permissions.Members.View).WithSummary("Church-journey timeline");
    }

    private static async Task<IQueryable<Person>> Filtered(MemberQuery q, PeopleDbContext db, MemberScope scope, CancellationToken ct)
    {
        var query = await scope.ApplyAsync(db.People.AsNoTracking(), ct);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern) ||
                EF.Functions.ILike(p.Email ?? "", pattern) ||
                EF.Functions.ILike(p.PhoneNumber ?? "", pattern) ||
                EF.Functions.ILike(p.MemberNumber, pattern));
        }

        if (Enum.TryParse<RecordStatus>(q.Status, true, out var status)) query = query.Where(p => p.Status == status);
        if (q.ParishId is { } parishId) query = query.Where(p => p.UnitId == parishId);
        return query;
    }

    private static IQueryable<Person> Sorted(IQueryable<Person> query, PageRequest page) => (page.Sort, page.Descending) switch
    {
        ("firstName", false) => query.OrderBy(p => p.FirstName).ThenBy(p => p.LastName),
        ("firstName", true) => query.OrderByDescending(p => p.FirstName).ThenByDescending(p => p.LastName),
        ("memberNumber", false) => query.OrderBy(p => p.MemberNumber),
        ("memberNumber", true) => query.OrderByDescending(p => p.MemberNumber),
        ("joinedAt", false) => query.OrderBy(p => p.CreatedAt),
        ("joinedAt", true) => query.OrderByDescending(p => p.CreatedAt),
        ("status", false) => query.OrderBy(p => p.Status),
        ("status", true) => query.OrderByDescending(p => p.Status),
        ("email", false) => query.OrderBy(p => p.Email),
        ("email", true) => query.OrderByDescending(p => p.Email),
        ("lastName", true) => query.OrderByDescending(p => p.LastName).ThenByDescending(p => p.FirstName),
        _ => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName),
    };

    private static DateTimeOffset JoinedAt(Person p) =>
        p.MembershipDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : p.CreatedAt;

    private static string StatusName(RecordStatus s) => s.ToString().ToLowerInvariant();

    internal static MemberSummaryResponse ToSummary(Person p, IReadOnlyDictionary<Guid, UnitSummary> units) => new(
        p.Id, p.MemberNumber, p.FirstName, p.LastName, p.Email, p.PhoneNumber, p.PhotoUrl, Parish(p, units), p.Rank, StatusName(p.Status),
        p.EmailConsent, JoinedAt(p));

    internal static async Task<MemberResponse> ToResponseAsync(Person p, IUnitDirectory units, IUserDirectory users, CancellationToken ct)
    {
        var unitMap = p.UnitId is { } u ? await units.GetAsync([u], ct) : new Dictionary<Guid, UnitSummary>();
        var creator = p.CreatedBy is { } c && (await users.GetAsync([c], ct)).TryGetValue(c, out var summary) ? new CreatorRef(summary.Id, summary.Name) : null;
        return new MemberResponse(
            p.Id, p.MemberNumber, p.FirstName, p.LastName, p.Email, p.PhoneNumber, p.PhotoUrl, Parish(p, unitMap), p.Rank, StatusName(p.Status),
            p.EmailConsent, JoinedAt(p), p.Gender == Gender.Unspecified ? null : p.Gender.ToString().ToLowerInvariant(), p.DateOfBirth,
            string.IsNullOrWhiteSpace(p.Address.Line1) ? null : p.Address.Line1, p.Notes, p.CreatedAt, p.UpdatedAt ?? p.CreatedAt, creator,
            p.MembershipStatus.ToString().ToLowerInvariant(), p.Tags, p.HouseholdId, p.UserId is not null);
    }

    private static ParishRef? Parish(Person p, IReadOnlyDictionary<Guid, UnitSummary> units) =>
        p.UnitId is { } id && units.TryGetValue(id, out var unit) ? new ParishRef(unit.Id, unit.Name) : null;

    private static async Task<IResult> List([AsParameters] MemberQuery q, PeopleDbContext db, MemberScope scope, IUnitDirectory units, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize, q.Q, q.Sort, q.Dir);
        var query = await Filtered(q, db, scope, ct);
        var total = await query.LongCountAsync(ct);
        var people = await Sorted(query, page).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var unitMap = await units.GetAsync(people.Where(p => p.UnitId != null).Select(p => p.UnitId!.Value), ct);
        return Results.Ok(new PagedResult<MemberSummaryResponse>(people.Select(p => ToSummary(p, unitMap)).ToList(), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> KeysetList(
        [AsParameters] KeysetRequest<string> request,
        PeopleDbContext db,
        MemberScope scope,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var limit = request.SafeLimit;
        var query = await scope.ApplyAsync(db.People.AsNoTracking(), ct);

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            var parts = request.Cursor.Split('_', 2);
            if (parts.Length == 2 && DateTimeOffset.TryParse(parts[0], out var cursorDate) && Guid.TryParse(parts[1], out var cursorId))
            {
                if (request.Ascending)
                {
                    query = query.Where(p => p.CreatedAt > cursorDate || (p.CreatedAt == cursorDate && p.Id.CompareTo(cursorId) > 0));
                }
                else
                {
                    query = query.Where(p => p.CreatedAt < cursorDate || (p.CreatedAt == cursorDate && p.Id.CompareTo(cursorId) < 0));
                }
            }
        }

        query = request.Ascending
            ? query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
            : query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id);

        var fetched = await query.Take(limit + 1).ToListAsync(ct);
        var hasMore = fetched.Count > limit;
        var people = hasMore ? fetched.Take(limit).ToList() : fetched;

        string? nextCursor = null;
        if (hasMore && people.Count > 0)
        {
            var last = people[^1];
            nextCursor = $"{last.CreatedAt:O}_{last.Id}";
        }

        var unitMap = await units.GetAsync(people.Where(p => p.UnitId != null).Select(p => p.UnitId!.Value), ct);
        var summaries = people.Select(p => ToSummary(p, unitMap)).ToList();
        return Results.Ok(new KeysetResponse<MemberSummaryResponse, string>(summaries, nextCursor, hasMore));
    }

    private static async Task<Person?> FindAsync(Guid id, PeopleDbContext db, MemberScope scope, bool tracking, CancellationToken ct)
    {
        var query = await scope.ApplyAsync(tracking ? db.People : db.People.AsNoTracking(), ct);
        return await query.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    private static async Task<IResult> Get(Guid id, PeopleDbContext db, MemberScope scope, IUnitDirectory units, IUserDirectory users, CancellationToken ct) =>
        await FindAsync(id, db, scope, false, ct) is { } person ? Results.Ok(await ToResponseAsync(person, units, users, ct)) : NotFound.ToError();

    private static async Task<Result> CheckParishAsync(Guid parishId, MemberScope scope, IUnitDirectory units, CancellationToken ct)
    {
        var parishes = await units.GetParishesAsync(null, ct);
        if (parishes.All(p => p.Id != parishId) || !await scope.CanUseUnitAsync(parishId, ct))
        {
            return Error.Validation("member.invalid_parish", "Choose a parish.", new Dictionary<string, string[]> { ["parishId"] = ["Choose a parish."] });
        }

        return Result.Success();
    }

    private static async Task<IResult> Create(MemberInput r, PeopleDbContext db, PersonFactory factory, MemberScope scope, IUnitDirectory units,
        IUserDirectory users, ICurrentUser caller, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        if ((await CheckParishAsync(r.ParishId, scope, units, ct)) is { IsFailure: true } invalid)
        {
            return invalid.Error.ToError();
        }

        if (await DuplicateEmailAsync(db, r.Email, null, ct))
        {
            return DuplicateEmail.ToError();
        }

        var person = await factory.CreateAsync(r.FirstName, r.LastName, MembershipStatus.Member, ct);
        Apply(person, r);
        person.SetEmailConsent(r.EmailConsent && person.Email is not null, caller.MembershipId, "admin", clock.GetUtcNow());
        db.People.Add(person);
        audit.Record("member.created", $"Added member {person.FirstName} {person.LastName}", target: Target(person));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/members/{person.Id}", await ToResponseAsync(person, units, users, ct));
    }

    private static async Task<IResult> Update(Guid id, MemberInput r, PeopleDbContext db, MemberScope scope, IUnitDirectory units, IUserDirectory users,
        ICurrentUser caller, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var person = await FindAsync(id, db, scope, true, ct);
        if (person is null)
        {
            return NotFound.ToError();
        }

        if ((await CheckParishAsync(r.ParishId, scope, units, ct)) is { IsFailure: true } invalid)
        {
            return invalid.Error.ToError();
        }

        if (await DuplicateEmailAsync(db, r.Email, id, ct))
        {
            return DuplicateEmail.ToError();
        }

        var before = new { person.FirstName, person.LastName, person.Email, Phone = person.PhoneNumber, person.UnitId, person.Rank, person.Status, person.EmailConsent };
        Apply(person, r);
        person.SetEmailConsent(r.EmailConsent && person.Email is not null, caller.MembershipId, "admin", clock.GetUtcNow());
        audit.Record("member.updated", $"Updated member {person.FirstName} {person.LastName}", target: Target(person),
            changes: AuditChanges.Diff(("firstName", before.FirstName, person.FirstName), ("lastName", before.LastName, person.LastName),
                ("email", before.Email, person.Email), ("phone", before.Phone, person.PhoneNumber), ("parishId", before.UnitId, person.UnitId),
                ("rank", before.Rank, person.Rank), ("status", StatusName(before.Status), StatusName(person.Status)),
                ("emailConsent", before.EmailConsent, person.EmailConsent)));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(person, units, users, ct));
    }

    private static readonly Error DuplicateEmail = Error.Conflict("member.email_taken", "Another member already uses that email address.",
        new Dictionary<string, string[]> { ["email"] = ["Another member already uses that email address."] });

    private static async Task<bool> DuplicateEmailAsync(PeopleDbContext db, string? email, Guid? exceptId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalized = email.Trim().ToLowerInvariant();
        return await db.People.AnyAsync(p => p.Email == normalized && p.Id != exceptId, ct);
    }

    private static void Apply(Person person, MemberInput r) =>
        person.UpdateRecord(r.FirstName, r.LastName, r.Email, r.Phone, r.ParishId, r.Rank,
            r.Gender switch { "female" => Gender.Female, "male" => Gender.Male, _ => Gender.Unspecified },
            r.DateOfBirth, r.Address, r.Notes, Enum.Parse<RecordStatus>(r.Status, true));

    private static async Task<IResult> Bulk(BulkMembersRequest r, PeopleDbContext db, MemberScope scope, IAuditLog audit, CancellationToken ct)
    {
        var ids = r.Ids.Distinct().ToList();
        var people = await (await scope.ApplyAsync(db.People, ct)).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var status = r.Action == "approve" ? RecordStatus.Active : RecordStatus.Inactive;
        people.ForEach(p => p.SetStatus(status));
        audit.Record(r.Action == "approve" ? "member.approved" : "member.deactivated",
            $"{(r.Action == "approve" ? "Approved" : "Deactivated")} {people.Count} member{(people.Count == 1 ? "" : "s")}");
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { updated = people.Count });
    }

    private static async Task<IResult> Delete(Guid id, PeopleDbContext db, MemberScope scope, IAuditLog audit, CancellationToken ct)
    {
        var person = await FindAsync(id, db, scope, true, ct);
        if (person is null)
        {
            return NotFound.ToError();
        }

        db.People.Remove(person);
        audit.Record("member.deleted", $"Deleted member {person.FirstName} {person.LastName}", AuditSeverity.Warning, Target(person));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> BulkDelete(BulkDeleteRequest r, PeopleDbContext db, MemberScope scope, IAuditLog audit, CancellationToken ct)
    {
        var ids = r.Ids.Distinct().ToList();
        var people = await (await scope.ApplyAsync(db.People, ct)).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        db.People.RemoveRange(people);
        audit.Record("member.deleted", $"Deleted {people.Count} member{(people.Count == 1 ? "" : "s")}", AuditSeverity.Warning);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { deleted = people.Count });
    }

    private static async Task<IResult> Export([AsParameters] MemberQuery q, PeopleDbContext db, MemberScope scope, IUnitDirectory units, IAuditLog audit,
        CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize, q.Q, q.Sort, q.Dir);
        var people = await Sorted(await Filtered(q, db, scope, ct), page).Take(100_000).ToListAsync(ct);
        var unitMap = await units.GetAsync(people.Where(p => p.UnitId != null).Select(p => p.UnitId!.Value), ct);
        audit.Record("member.exported", $"Exported {people.Count} members", AuditSeverity.Warning);
        await audit.FlushAsync(ct);

        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Member ID", "First name", "Last name", "Email", "Phone", "Parish", "Rank", "Status", "Email consent", "Gender", "Date of birth", "Address", "Joined" },
        };
        rows.AddRange(people.Select(p => (IReadOnlyList<object?>)
        [
            p.MemberNumber, p.FirstName, p.LastName, p.Email, p.PhoneNumber, Parish(p, unitMap)?.Name, p.Rank, StatusName(p.Status), p.EmailConsent,
            p.Gender == Gender.Unspecified ? null : p.Gender.ToString(), p.DateOfBirth, p.Address.Line1, JoinedAt(p),
        ]));
        return new CsvResult($"members-{DateTime.UtcNow:yyyyMMdd}.csv", rows);
    }

    private static async Task<IResult> ChangeStage(Guid id, ChangeStageRequest r, PeopleDbContext db, MemberScope scope, IUnitDirectory units,
        IUserDirectory users, IAuditLog audit, TimeProvider clock, CancellationToken ct)
    {
        var person = await FindAsync(id, db, scope, true, ct);
        if (person is null)
        {
            return NotFound.ToError();
        }

        if (!Enum.IsDefined(r.Stage))
        {
            return Error.Validation("member.invalid_stage", "Choose a stage.").ToError();
        }

        var from = person.MembershipStatus;
        person.ChangeMembershipStatus(r.Stage, r.EffectiveDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), r.Reason);
        audit.Record("member.stage_changed", $"Moved {person.FirstName} {person.LastName} from {from} to {r.Stage}", target: Target(person));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToResponseAsync(person, units, users, ct));
    }

    private static async Task<IResult> StageHistory(Guid id, PeopleDbContext db, MemberScope scope, CancellationToken ct)
    {
        if (await FindAsync(id, db, scope, false, ct) is null)
        {
            return NotFound.ToError();
        }

        return Results.Ok(await db.StatusChanges.AsNoTracking().Where(s => s.PersonId == id)
            .OrderByDescending(s => s.EffectiveDate).ThenByDescending(s => s.CreatedAt)
            .Select(s => new StatusChangeResponse(s.Id, s.From.ToString(), s.To.ToString(), s.EffectiveDate, s.Reason, s.CreatedAt, s.CreatedBy))
            .ToListAsync(ct));
    }

    private static AuditTarget Target(Person p) => new("member", p.Id.ToString(), $"{p.FirstName} {p.LastName}");

    private static async Task<IResult> Stats(PeopleDbContext db, MemberScope scope, TimeProvider clock, CancellationToken ct) =>
        Results.Ok(await MemberStats.ComputeAsync(await scope.ApplyAsync(db.People.AsNoTracking(), ct), clock.GetUtcNow(), ct));
}
