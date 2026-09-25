using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.People.Features;

public sealed record PersonListItem(
    Guid Id, string MemberNumber, string FullName, string? Email, string? PhoneNumber, string? PhotoUrl, string MembershipStatus,
    string Gender, DateOnly? DateOfBirth, Guid? BranchId, Guid? HouseholdId, IReadOnlyList<string> Tags, bool HasAccount);

public sealed record PersonResponse(
    Guid Id, string MemberNumber, Guid? BranchId, Guid? HouseholdId, string? HouseholdRole, Guid? UserId,
    string? Title, string FirstName, string? MiddleName, string LastName, string? PreferredName, string FullName,
    string Gender, DateOnly? DateOfBirth, string MaritalStatus, DateOnly? WeddingAnniversary,
    string? Email, string? PhoneNumber, string? AlternatePhoneNumber, Address Address, string? Occupation, string? Employer,
    string? PhotoUrl, string MembershipStatus, DateOnly? FirstVisitDate, DateOnly? MembershipDate, DateOnly? SalvationDate,
    DateOnly? BaptismDate, string? Source, bool ConsentToContact, IReadOnlyList<string> Tags, JsonElement CustomFields,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record SavePersonRequest(
    string? Title, string FirstName, string? MiddleName, string LastName, string? PreferredName, Gender Gender,
    DateOnly? DateOfBirth, MaritalStatus MaritalStatus, DateOnly? WeddingAnniversary, string? Email, string? PhoneNumber,
    string? AlternatePhoneNumber, Address? Address, string? Occupation, string? Employer, string? PhotoUrl, string? Source,
    bool ConsentToContact, DateOnly? SalvationDate, DateOnly? BaptismDate, DateOnly? FirstVisitDate, Guid? BranchId,
    IReadOnlyList<string>? Tags, Dictionary<string, JsonElement>? CustomFields,
    MembershipStatus MembershipStatus = MembershipStatus.Visitor)
{
    public PersonProfile ToProfile() => new(
        Title, FirstName, MiddleName, LastName, PreferredName, Gender, DateOfBirth, MaritalStatus, WeddingAnniversary, Email,
        PhoneNumber, AlternatePhoneNumber, Address, Occupation, Employer, PhotoUrl, Source, ConsentToContact, SalvationDate,
        BaptismDate, FirstVisitDate, BranchId, Tags);
}

public sealed record ChangeStatusRequest(MembershipStatus Status, DateOnly? EffectiveDate, string? Reason);

public sealed record StatusChangeResponse(Guid Id, string From, string To, DateOnly EffectiveDate, string? Reason, DateTimeOffset RecordedAt, Guid? RecordedBy);

public sealed record PeopleStatsResponse(
    long Total, IReadOnlyDictionary<string, long> ByStatus, long NewThisMonth, long WithAccounts,
    IReadOnlyList<CelebrationItem> UpcomingBirthdays, IReadOnlyList<CelebrationItem> UpcomingAnniversaries);

public sealed record CelebrationItem(Guid PersonId, string FullName, DateOnly Date, string? PhotoUrl);

public sealed record PeopleQuery(
    int Page = 1, int PageSize = 25, string? Search = null, MembershipStatus? Status = null, Guid? BranchId = null,
    string? Tag = null, Gender? Gender = null, string? Sort = null);

internal sealed class SavePersonValidator : AbstractValidator<SavePersonRequest>
{
    public SavePersonValidator()
    {
        RuleFor(x => x.Title).MaximumLength(32);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MiddleName).MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PreferredName).MaximumLength(100);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.MaritalStatus).IsInEnum();
        RuleFor(x => x.MembershipStatus).IsInEnum();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.AlternatePhoneNumber).MaximumLength(32);
        RuleFor(x => x.Occupation).MaximumLength(100);
        RuleFor(x => x.Employer).MaximumLength(200);
        RuleFor(x => x.PhotoUrl).MaximumLength(1024);
        RuleFor(x => x.Source).MaximumLength(100);
        RuleFor(x => x.DateOfBirth).LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.Tags).Must(t => t is null || t.Count <= 50).WithMessage("At most 50 tags.");
        RuleForEach(x => x.Tags).MaximumLength(50);
    }
}

/// <summary>People directory: the church's membership database.</summary>
public static class PeopleEndpoints
{
    internal static readonly Error NotFound = Error.NotFound("person.not_found", "The person was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("people", "People");

        group.MapGet("/", List).RequirePermission(Permissions.People.Read).WithSummary("Search people");
        group.MapGet("/stats", Stats).RequirePermission(Permissions.People.Read).WithSummary("Membership dashboard figures");
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.People.Read).WithSummary("Get a person");
        group.MapPost("/", Create).WithValidation<SavePersonRequest>().RequirePermission(Permissions.People.Write).WithSummary("Add a person");
        group.MapPut("/{id:guid}", Update).WithValidation<SavePersonRequest>().RequirePermission(Permissions.People.Write).WithSummary("Update a person");
        group.MapDelete("/{id:guid}", Delete).RequirePermission(Permissions.People.Delete).WithSummary("Archive (soft delete) a person");
        group.MapPost("/{id:guid}/status", ChangeStatus).RequirePermission(Permissions.People.Write).WithSummary("Change membership status");
        group.MapGet("/{id:guid}/status-history", StatusHistory).RequirePermission(Permissions.People.Read).WithSummary("Membership status timeline");
    }

    internal static PersonResponse ToResponse(this Person p) => new(
        p.Id, p.MemberNumber, p.BranchId, p.HouseholdId, p.HouseholdRole?.ToString(), p.UserId, p.Title, p.FirstName, p.MiddleName,
        p.LastName, p.PreferredName, p.FullName, p.Gender.ToString(), p.DateOfBirth, p.MaritalStatus.ToString(), p.WeddingAnniversary,
        p.Email, p.PhoneNumber, p.AlternatePhoneNumber, p.Address, p.Occupation, p.Employer, p.PhotoUrl, p.MembershipStatus.ToString(),
        p.FirstVisitDate, p.MembershipDate, p.SalvationDate, p.BaptismDate, p.Source, p.ConsentToContact, p.Tags,
        JsonDocument.Parse(p.CustomFields).RootElement.Clone(), p.CreatedAt, p.UpdatedAt);

    private static async Task<IResult> List([AsParameters] PeopleQuery q, PeopleDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.People.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            var pattern = $"%{term}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.FirstName + " " + p.LastName, pattern) ||
                EF.Functions.ILike(p.PreferredName ?? "", pattern) ||
                EF.Functions.ILike(p.Email ?? "", pattern) ||
                EF.Functions.ILike(p.PhoneNumber ?? "", pattern) ||
                p.MemberNumber == term.ToUpperInvariant());
        }

        if (q.Status is { } status) query = query.Where(p => p.MembershipStatus == status);
        if (q.BranchId is { } branchId) query = query.Where(p => p.BranchId == branchId);
        if (q.Gender is { } gender) query = query.Where(p => p.Gender == gender);
        if (!string.IsNullOrWhiteSpace(q.Tag)) query = query.Where(p => p.Tags.Contains(q.Tag.ToLowerInvariant()));

        query = q.Sort switch
        {
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            "memberNumber" => query.OrderBy(p => p.MemberNumber),
            "firstName" => query.OrderBy(p => p.FirstName).ThenBy(p => p.LastName),
            _ => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName),
        };

        var total = await query.LongCountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.SafePageSize)
            .Select(p => new PersonListItem(
                p.Id, p.MemberNumber, (p.PreferredName ?? p.FirstName) + " " + p.LastName, p.Email, p.PhoneNumber, p.PhotoUrl,
                p.MembershipStatus.ToString(), p.Gender.ToString(), p.DateOfBirth, p.BranchId, p.HouseholdId, p.Tags, p.UserId != null))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<PersonListItem>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Get(Guid id, PeopleDbContext db, CancellationToken ct) =>
        await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } person
            ? Results.Ok(person.ToResponse())
            : NotFound.ToProblem();

    private static async Task<IResult> Create(
        SavePersonRequest request, PeopleDbContext db, PersonFactory factory, CustomFieldValidator customFields, CancellationToken ct)
    {
        var fields = await customFields.ValidateAsync(request.CustomFields, ct);
        if (fields.IsFailure)
        {
            return fields.Error.ToProblem();
        }

        var person = await factory.CreateAsync(request.FirstName, request.LastName, request.MembershipStatus, ct);
        person.UpdateProfile(request.ToProfile());
        person.SetCustomFields(fields.Value);
        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/people/{person.Id}", person.ToResponse());
    }

    private static async Task<IResult> Update(
        Guid id, SavePersonRequest request, PeopleDbContext db, CustomFieldValidator customFields, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
        {
            return NotFound.ToProblem();
        }

        var fields = await customFields.ValidateAsync(request.CustomFields, ct);
        if (fields.IsFailure)
        {
            return fields.Error.ToProblem();
        }

        person.UpdateProfile(request.ToProfile());
        person.SetCustomFields(fields.Value);
        await db.SaveChangesAsync(ct);
        return Results.Ok(person.ToResponse());
    }

    private static async Task<IResult> Delete(Guid id, PeopleDbContext db, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
        {
            return NotFound.ToProblem();
        }

        db.People.Remove(person);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeStatus(Guid id, ChangeStatusRequest request, PeopleDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null)
        {
            return NotFound.ToProblem();
        }

        if (!Enum.IsDefined(request.Status))
        {
            return Error.Validation("person.invalid_status", "Unknown membership status.").ToProblem();
        }

        person.ChangeMembershipStatus(request.Status, request.EffectiveDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), request.Reason);
        await db.SaveChangesAsync(ct);
        return Results.Ok(person.ToResponse());
    }

    private static async Task<IResult> StatusHistory(Guid id, PeopleDbContext db, CancellationToken ct) =>
        Results.Ok(await db.StatusChanges.AsNoTracking().Where(s => s.PersonId == id)
            .OrderByDescending(s => s.EffectiveDate).ThenByDescending(s => s.CreatedAt)
            .Select(s => new StatusChangeResponse(s.Id, s.From.ToString(), s.To.ToString(), s.EffectiveDate, s.Reason, s.CreatedAt, s.CreatedBy))
            .ToListAsync(ct));

    private static async Task<IResult> Stats(PeopleDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var byStatus = await db.People.AsNoTracking().GroupBy(p => p.MembershipStatus)
            .Select(g => new { Status = g.Key, Count = g.LongCount() }).ToListAsync(ct);
        var newThisMonth = await db.People.LongCountAsync(p => p.CreatedAt >= monthStart, ct);
        var withAccounts = await db.People.LongCountAsync(p => p.UserId != null, ct);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i)).Select(d => d.Month * 100 + d.Day).ToList();

        var birthdays = await db.People.AsNoTracking()
            .Where(p => p.DateOfBirth != null && days.Contains(p.DateOfBirth.Value.Month * 100 + p.DateOfBirth.Value.Day))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName, Date = p.DateOfBirth!.Value, p.PhotoUrl })
            .OrderBy(p => p.Id)
            .Take(200).ToListAsync(ct);
        var anniversaries = await db.People.AsNoTracking()
            .Where(p => p.WeddingAnniversary != null && days.Contains(p.WeddingAnniversary.Value.Month * 100 + p.WeddingAnniversary.Value.Day))
            .Select(p => new { p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName, Date = p.WeddingAnniversary!.Value, p.PhotoUrl })
            .OrderBy(p => p.Id)
            .Take(200).ToListAsync(ct);

        IReadOnlyList<CelebrationItem> Order(IEnumerable<(Guid Id, string Name, DateOnly Date, string? PhotoUrl)> items) =>
            items.OrderBy(x => days.IndexOf(x.Date.Month * 100 + x.Date.Day))
                .Select(x => new CelebrationItem(x.Id, x.Name, x.Date, x.PhotoUrl)).ToList();

        return Results.Ok(new PeopleStatsResponse(
            byStatus.Sum(s => s.Count), byStatus.ToDictionary(s => s.Status.ToString(), s => s.Count), newThisMonth, withAccounts,
            Order(birthdays.Select(b => (b.Id, b.Name, b.Date, b.PhotoUrl))),
            Order(anniversaries.Select(a => (a.Id, a.Name, a.Date, a.PhotoUrl)))));
    }
}

/// <summary>Validates custom field values against the tenant's active definitions and normalises to JSON.</summary>
internal sealed class CustomFieldValidator(PeopleDbContext db)
{
    public async Task<Result<string>> ValidateAsync(Dictionary<string, JsonElement>? values, CancellationToken ct)
    {
        values ??= [];
        var definitions = await db.CustomFields.AsNoTracking().Where(f => f.IsActive).ToListAsync(ct);
        var errors = new Dictionary<string, string[]>();
        var clean = new Dictionary<string, JsonElement>();

        foreach (var def in definitions)
        {
            var present = values.TryGetValue(def.Key, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            if (!present)
            {
                if (def.IsRequired)
                {
                    errors[$"customFields.{def.Key}"] = [$"{def.Label} is required."];
                }

                continue;
            }

            var ok = def.FieldType switch
            {
                CustomFieldType.Text or CustomFieldType.LongText => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 4000,
                CustomFieldType.Number => value.ValueKind == JsonValueKind.Number,
                CustomFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                CustomFieldType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out _),
                CustomFieldType.Select => value.ValueKind == JsonValueKind.String && def.Options.Contains(value.GetString()!),
                CustomFieldType.MultiSelect => value.ValueKind == JsonValueKind.Array &&
                    value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && def.Options.Contains(v.GetString()!)),
                _ => false,
            };

            if (!ok)
            {
                errors[$"customFields.{def.Key}"] = [$"Invalid value for {def.Label}."];
                continue;
            }

            clean[def.Key] = value;
        }

        if (errors.Count > 0)
        {
            return Error.Validation("person.invalid_custom_fields", "Some custom fields are invalid.", errors);
        }

        return JsonSerializer.Serialize(clean);
    }
}
