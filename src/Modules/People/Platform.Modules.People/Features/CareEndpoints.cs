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

public sealed record HouseholdResponse(Guid Id, string Name, string? PhoneNumber, Address Address, Guid? PrimaryContactPersonId,
    IReadOnlyList<HouseholdMemberResponse> Members);

public sealed record HouseholdMemberResponse(Guid PersonId, string FullName, string? Role, string? PhotoUrl, DateOnly? DateOfBirth);

public sealed record SaveHouseholdRequest(string Name, string? PhoneNumber, Address? Address, Guid? PrimaryContactPersonId);

public sealed record AddHouseholdMemberRequest(Guid PersonId, HouseholdRole Role);

public sealed record NoteResponse(Guid Id, Guid PersonId, string Category, string Visibility, string Body, Guid? AuthorUserId, DateTimeOffset CreatedAt);

public sealed record CreateNoteRequest(NoteCategory Category, NoteVisibility Visibility, string Body);

public sealed record FollowUpResponse(Guid Id, Guid PersonId, string PersonName, string Type, string Status, string Priority,
    Guid? AssignedToUserId, DateOnly? DueDate, string? Notes, string? Outcome, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed record SaveFollowUpRequest(Guid PersonId, FollowUpType Type, Priority Priority, Guid? AssignedToUserId, DateOnly? DueDate, string? Notes);

public sealed record FollowUpStatusRequest(FollowUpStatus Status, string? Outcome);

public sealed record FollowUpQuery(int Page = 1, int PageSize = 25, FollowUpStatus? Status = null, bool Mine = false, Guid? PersonId = null, bool Overdue = false);

public sealed record CustomFieldResponse(Guid Id, string Key, string Label, string FieldType, IReadOnlyList<string> Options, bool IsRequired, bool IsActive, int SortOrder);

public sealed record SaveCustomFieldRequest(string Key, string Label, CustomFieldType FieldType, IReadOnlyList<string>? Options, bool IsRequired, bool IsActive = true, int SortOrder = 0);

internal sealed class SaveHouseholdValidator : AbstractValidator<SaveHouseholdRequest>
{
    public SaveHouseholdValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
    }
}

internal sealed class CreateNoteValidator : AbstractValidator<CreateNoteRequest>
{
    public CreateNoteValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
    }
}

internal sealed class SaveFollowUpValidator : AbstractValidator<SaveFollowUpRequest>
{
    public SaveFollowUpValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(4000);
    }
}

internal sealed class SaveCustomFieldValidator : AbstractValidator<SaveCustomFieldRequest>
{
    public SaveCustomFieldValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(64).Matches("^[a-z][a-z0-9_]*$").WithMessage("Use lowercase letters, digits and underscores.");
        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FieldType).IsInEnum();
        RuleFor(x => x.Options).NotEmpty().When(x => x.FieldType is CustomFieldType.Select or CustomFieldType.MultiSelect)
            .WithMessage("Select fields need at least one option.");
    }
}

/// <summary>Households, pastoral notes, follow-up tasks and custom profile fields.</summary>
public static class CareEndpoints
{
    private static readonly Error HouseholdNotFound = Error.NotFound("household.not_found", "The household was not found.");
    private static readonly ILookup<Guid, HouseholdMemberResponse> NoMembers =
        Array.Empty<HouseholdMemberResponse>().ToLookup(_ => Guid.Empty);

    private static readonly Error FollowUpNotFound = Error.NotFound("followup.not_found", "The follow-up was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var households = endpoints.MapModuleGroup("households", "People");
        households.MapGet("/", ListHouseholds).RequirePermission(Permissions.People.Read).WithSummary("List households");
        households.MapGet("/{id:guid}", GetHousehold).RequirePermission(Permissions.People.Read).WithSummary("Get a household with members");
        households.MapPost("/", CreateHousehold).WithValidation<SaveHouseholdRequest>().RequirePermission(Permissions.People.HouseholdsManage).WithSummary("Create a household");
        households.MapPut("/{id:guid}", UpdateHousehold).WithValidation<SaveHouseholdRequest>().RequirePermission(Permissions.People.HouseholdsManage).WithSummary("Update a household");
        households.MapDelete("/{id:guid}", DeleteHousehold).RequirePermission(Permissions.People.HouseholdsManage).WithSummary("Delete a household (members are kept)");
        households.MapPost("/{id:guid}/members", AddMember).RequirePermission(Permissions.People.HouseholdsManage).WithSummary("Add a person to a household");
        households.MapDelete("/{id:guid}/members/{personId:guid}", RemoveMember).RequirePermission(Permissions.People.HouseholdsManage).WithSummary("Remove a person from a household");

        var people = endpoints.MapModuleGroup("people", "People");
        people.MapGet("/{personId:guid}/notes", ListNotes).RequirePermission(Permissions.People.NotesRead).WithSummary("Pastoral notes for a person");
        people.MapPost("/{personId:guid}/notes", CreateNote).WithValidation<CreateNoteRequest>().RequirePermission(Permissions.People.NotesWrite).WithSummary("Add a note");
        people.MapDelete("/{personId:guid}/notes/{noteId:guid}", DeleteNote).RequirePermission(Permissions.People.NotesWrite).WithSummary("Delete one of my notes");

        var followUps = endpoints.MapModuleGroup("follow-ups", "Pastoral care");
        followUps.MapGet("/", ListFollowUps).RequirePermission(Permissions.People.Read).WithSummary("Follow-up tasks");
        followUps.MapPost("/", CreateFollowUp).WithValidation<SaveFollowUpRequest>().RequirePermission(Permissions.People.FollowUpsManage).WithSummary("Create a follow-up");
        followUps.MapPut("/{id:guid}", UpdateFollowUp).WithValidation<SaveFollowUpRequest>().RequirePermission(Permissions.People.FollowUpsManage).WithSummary("Update a follow-up");
        followUps.MapPost("/{id:guid}/status", ChangeFollowUpStatus).RequirePermission(Permissions.People.FollowUpsManage).WithSummary("Progress or close a follow-up");

        var fields = endpoints.MapModuleGroup("people/custom-fields", "People");
        fields.MapGet("/", ListCustomFields).RequirePermission(Permissions.People.Read).WithSummary("Custom profile fields");
        fields.MapPost("/", CreateCustomField).WithValidation<SaveCustomFieldRequest>().RequirePermission(Permissions.People.CustomFieldsManage).WithSummary("Define a custom field");
        fields.MapPut("/{id:guid}", UpdateCustomField).WithValidation<SaveCustomFieldRequest>().RequirePermission(Permissions.People.CustomFieldsManage).WithSummary("Update a custom field");
        fields.MapDelete("/{id:guid}", DeleteCustomField).RequirePermission(Permissions.People.CustomFieldsManage).WithSummary("Remove a custom field");
    }

    // ---- Households ------------------------------------------------------------------------

    private static async Task<IResult> ListHouseholds([AsParameters] PageRequest page, string? search, PeopleDbContext db, CancellationToken ct)
    {
        var query = db.Households.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(h => EF.Functions.ILike(h.Name, $"%{search.Trim()}%"));
        }

        var total = await query.LongCountAsync(ct);
        var households = await query.OrderBy(h => h.Name).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var ids = households.Select(h => h.Id).ToList();
        var members = await MembersAsync(db, ids, ct);

        var items = households.Select(h => ToResponse(h, members)).ToList();
        return Results.Ok(new PagedResult<HouseholdResponse>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> GetHousehold(Guid id, PeopleDbContext db, CancellationToken ct)
    {
        var household = await db.Households.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        return household is null ? HouseholdNotFound.ToProblem() : Results.Ok(ToResponse(household, await MembersAsync(db, [id], ct)));
    }

    private static async Task<IResult> CreateHousehold(SaveHouseholdRequest r, PeopleDbContext db, CancellationToken ct)
    {
        var household = Household.Create(r.Name);
        household.Update(r.Name, r.PhoneNumber, r.Address, r.PrimaryContactPersonId);
        db.Households.Add(household);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/households/{household.Id}", ToResponse(household, NoMembers));
    }

    private static async Task<IResult> UpdateHousehold(Guid id, SaveHouseholdRequest r, PeopleDbContext db, CancellationToken ct)
    {
        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (household is null)
        {
            return HouseholdNotFound.ToProblem();
        }

        household.Update(r.Name, r.PhoneNumber, r.Address, r.PrimaryContactPersonId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(household, await MembersAsync(db, [id], ct)));
    }

    private static async Task<IResult> DeleteHousehold(Guid id, PeopleDbContext db, CancellationToken ct)
    {
        var household = await db.Households.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (household is null)
        {
            return HouseholdNotFound.ToProblem();
        }

        var members = await db.People.Where(p => p.HouseholdId == id).ToListAsync(ct);
        members.ForEach(p => p.LeaveHousehold());
        db.Households.Remove(household);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddMember(Guid id, AddHouseholdMemberRequest r, PeopleDbContext db, CancellationToken ct)
    {
        if (!await db.Households.AnyAsync(h => h.Id == id, ct))
        {
            return HouseholdNotFound.ToProblem();
        }

        var person = await db.People.FirstOrDefaultAsync(p => p.Id == r.PersonId, ct);
        if (person is null)
        {
            return PeopleEndpoints.NotFound.ToProblem();
        }

        person.JoinHousehold(id, r.Role);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(Guid id, Guid personId, PeopleDbContext db, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId && p.HouseholdId == id, ct);
        if (person is null)
        {
            return PeopleEndpoints.NotFound.ToProblem();
        }

        person.LeaveHousehold();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<ILookup<Guid, HouseholdMemberResponse>> MembersAsync(PeopleDbContext db, IReadOnlyCollection<Guid> householdIds, CancellationToken ct)
    {
        var rows = await db.People.AsNoTracking()
            .Where(p => p.HouseholdId != null && householdIds.Contains(p.HouseholdId.Value))
            .OrderBy(p => p.HouseholdRole).ThenBy(p => p.DateOfBirth)
            .Select(p => new { HouseholdId = p.HouseholdId!.Value, p.Id, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName, p.HouseholdRole, p.PhotoUrl, p.DateOfBirth })
            .ToListAsync(ct);

        return rows.ToLookup(r => r.HouseholdId, r => new HouseholdMemberResponse(r.Id, r.Name, r.HouseholdRole?.ToString(), r.PhotoUrl, r.DateOfBirth));
    }

    private static HouseholdResponse ToResponse(Household h, ILookup<Guid, HouseholdMemberResponse> members) =>
        new(h.Id, h.Name, h.PhoneNumber, h.Address, h.PrimaryContactPersonId, members[h.Id].ToList());

    // ---- Notes -----------------------------------------------------------------------------

    private static async Task<IResult> ListNotes(Guid personId, ICurrentUser user, PeopleDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Notes.AsNoTracking()
            .Where(n => n.PersonId == personId && (n.Visibility == NoteVisibility.Staff || n.CreatedBy == user.UserId))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NoteResponse(n.Id, n.PersonId, n.Category.ToString(), n.Visibility.ToString(), n.Body, n.CreatedBy, n.CreatedAt))
            .ToListAsync(ct));

    private static async Task<IResult> CreateNote(Guid personId, CreateNoteRequest r, PeopleDbContext db, CancellationToken ct)
    {
        if (!await db.People.AnyAsync(p => p.Id == personId, ct))
        {
            return PeopleEndpoints.NotFound.ToProblem();
        }

        var note = PersonNote.Create(personId, r.Category, r.Visibility, r.Body);
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/people/{personId}/notes/{note.Id}",
            new NoteResponse(note.Id, personId, note.Category.ToString(), note.Visibility.ToString(), note.Body, note.CreatedBy, note.CreatedAt));
    }

    private static async Task<IResult> DeleteNote(Guid personId, Guid noteId, ICurrentUser user, PeopleDbContext db, CancellationToken ct)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.PersonId == personId, ct);
        if (note is null)
        {
            return Error.NotFound("note.not_found", "The note was not found.").ToProblem();
        }

        if (note.CreatedBy != user.UserId)
        {
            return Error.Forbidden("note.not_author", "Only the author can delete a note.").ToProblem();
        }

        db.Notes.Remove(note);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Follow-ups ------------------------------------------------------------------------

    private static async Task<IResult> ListFollowUps([AsParameters] FollowUpQuery q, ICurrentUser user, PeopleDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = from f in db.FollowUps.AsNoTracking()
                    join p in db.People.AsNoTracking() on f.PersonId equals p.Id
                    select new { f, Name = (p.PreferredName ?? p.FirstName) + " " + p.LastName };

        if (q.Status is { } status) query = query.Where(x => x.f.Status == status);
        if (q.Mine) query = query.Where(x => x.f.AssignedToUserId == user.UserId);
        if (q.PersonId is { } personId) query = query.Where(x => x.f.PersonId == personId);
        if (q.Overdue)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            query = query.Where(x => x.f.DueDate < today && (x.f.Status == FollowUpStatus.Open || x.f.Status == FollowUpStatus.InProgress));
        }

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.f.Priority).ThenBy(x => x.f.DueDate).ThenBy(x => x.f.CreatedAt)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(x => new FollowUpResponse(x.f.Id, x.f.PersonId, x.Name, x.f.Type.ToString(), x.f.Status.ToString(), x.f.Priority.ToString(),
                x.f.AssignedToUserId, x.f.DueDate, x.f.Notes, x.f.Outcome, x.f.CreatedAt, x.f.CompletedAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<FollowUpResponse>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> CreateFollowUp(SaveFollowUpRequest r, PeopleDbContext db, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.PersonId, ct);
        if (person is null)
        {
            return PeopleEndpoints.NotFound.ToProblem();
        }

        var followUp = FollowUp.Create(r.PersonId, r.Type, r.Priority, r.AssignedToUserId, r.DueDate, r.Notes);
        db.FollowUps.Add(followUp);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/follow-ups/{followUp.Id}", ToResponse(followUp, person.FullName));
    }

    private static async Task<IResult> UpdateFollowUp(Guid id, SaveFollowUpRequest r, PeopleDbContext db, CancellationToken ct)
    {
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (followUp is null)
        {
            return FollowUpNotFound.ToProblem();
        }

        followUp.Update(r.Priority, r.AssignedToUserId, r.DueDate, r.Notes);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeFollowUpStatus(Guid id, FollowUpStatusRequest r, PeopleDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var followUp = await db.FollowUps.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (followUp is null)
        {
            return FollowUpNotFound.ToProblem();
        }

        followUp.ChangeStatus(r.Status, r.Outcome, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static FollowUpResponse ToResponse(FollowUp f, string personName) => new(
        f.Id, f.PersonId, personName, f.Type.ToString(), f.Status.ToString(), f.Priority.ToString(), f.AssignedToUserId, f.DueDate,
        f.Notes, f.Outcome, f.CreatedAt, f.CompletedAt);

    // ---- Custom fields ---------------------------------------------------------------------

    private static CustomFieldResponse ToResponse(CustomFieldDefinition f) =>
        new(f.Id, f.Key, f.Label, f.FieldType.ToString(), f.Options, f.IsRequired, f.IsActive, f.SortOrder);

    private static async Task<IResult> ListCustomFields(PeopleDbContext db, CancellationToken ct) =>
        Results.Ok((await db.CustomFields.AsNoTracking().OrderBy(f => f.SortOrder).ThenBy(f => f.Label).ToListAsync(ct)).Select(ToResponse));

    private static async Task<IResult> CreateCustomField(SaveCustomFieldRequest r, PeopleDbContext db, CancellationToken ct)
    {
        if (await db.CustomFields.AnyAsync(f => f.Key == r.Key, ct))
        {
            return Error.Conflict("customfield.key_taken", "A field with this key already exists.").ToProblem();
        }

        var field = CustomFieldDefinition.Create(r.Key, r.Label, r.FieldType, r.Options, r.IsRequired, r.SortOrder);
        db.CustomFields.Add(field);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/people/custom-fields/{field.Id}", ToResponse(field));
    }

    private static async Task<IResult> UpdateCustomField(Guid id, SaveCustomFieldRequest r, PeopleDbContext db, CancellationToken ct)
    {
        var field = await db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (field is null)
        {
            return Error.NotFound("customfield.not_found", "The field was not found.").ToProblem();
        }

        field.Update(r.Label, r.Options, r.IsRequired, r.IsActive, r.SortOrder);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(field));
    }

    private static async Task<IResult> DeleteCustomField(Guid id, PeopleDbContext db, CancellationToken ct)
    {
        var field = await db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (field is null)
        {
            return Error.NotFound("customfield.not_found", "The field was not found.").ToProblem();
        }

        db.CustomFields.Remove(field);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
