using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Modules.Groups.Domain;
using Platform.Modules.Groups.Infrastructure;
using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Groups.Features;

public sealed record GroupResponse(
    Guid Id, string Name, string Slug, string Type, string Visibility, Guid? ParentGroupId, Guid? BranchId, string? Description,
    string? ImageUrl, string? MeetingSchedule, string? MeetingLocation, int? Capacity, bool IsActive, bool AcceptsJoinRequests,
    int MemberCount, IReadOnlyList<GroupLeaderResponse> Leaders);

public sealed record GroupLeaderResponse(Guid PersonId, string FullName, string Role, string? PhotoUrl);

public sealed record GroupMemberResponse(Guid PersonId, string FullName, string? Email, string? PhoneNumber, string? PhotoUrl,
    string Role, string Status, DateOnly JoinedOn);

public sealed record SaveGroupRequest(
    string Name, string? Slug, GroupType Type, GroupVisibility Visibility, Guid? ParentGroupId, Guid? BranchId, string? Description,
    string? ImageUrl, string? MeetingSchedule, string? MeetingLocation, int? Capacity, bool IsActive = true, bool AcceptsJoinRequests = false);

public sealed record AddGroupMemberRequest(Guid PersonId, GroupMemberRole Role = GroupMemberRole.Member, GroupMemberStatus Status = GroupMemberStatus.Active);

public sealed record GroupQuery(int Page = 1, int PageSize = 50, string? Search = null, GroupType? Type = null, Guid? BranchId = null,
    Guid? ParentGroupId = null, bool IncludeInactive = false);

public sealed record PublicGroupResponse(Guid Id, string Name, string Slug, string Type, string? Description, string? ImageUrl,
    string? MeetingSchedule, string? MeetingLocation, bool AcceptsJoinRequests);

internal sealed class SaveGroupValidator : AbstractValidator<SaveGroupRequest>
{
    public SaveGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(s => s is null || Slug.IsValid(s)).WithMessage("Use lowercase letters, digits and hyphens.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Capacity).GreaterThan(0).When(x => x.Capacity.HasValue);
    }
}

public static class GroupEndpoints
{
    private static readonly Error NotFound = Error.NotFound("group.not_found", "The group was not found.");
    private static readonly Error SlugTaken = Error.Conflict("group.slug_taken", "Another group already uses this URL.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("groups", "Groups & ministries");
        group.MapGet("/", List).RequirePermission(Permissions.Groups.Read).WithSummary("List groups");
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.Groups.Read).WithSummary("Get a group");
        group.MapPost("/", Create).WithValidation<SaveGroupRequest>().RequirePermission(Permissions.Groups.Write).WithSummary("Create a group");
        group.MapPut("/{id:guid}", Update).WithValidation<SaveGroupRequest>().RequirePermission(Permissions.Groups.Write).WithSummary("Update a group");
        group.MapDelete("/{id:guid}", Delete).RequirePermission(Permissions.Groups.Delete).WithSummary("Archive a group");
        group.MapGet("/{id:guid}/members", ListMembers).RequirePermission(Permissions.Groups.Read).WithSummary("Group roster");
        group.MapPost("/{id:guid}/members", AddMember).RequirePermission(Permissions.Groups.MembersManage).WithSummary("Add or update a member");
        group.MapDelete("/{id:guid}/members/{personId:guid}", RemoveMember).RequirePermission(Permissions.Groups.MembersManage).WithSummary("Remove a member");
        group.MapGet("/by-person/{personId:guid}", ByPerson).RequirePermission(Permissions.Groups.Read).WithSummary("Groups a person belongs to");

        endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me/groups").WithTags("My account").RequireAuthorization()
            .MapGet("/", MyGroups).WithSummary("Groups I belong to");

        endpoints.MapPublicGroup("groups", "Public")
            .MapGet("/", PublicList).WithSummary("Public ministries and groups for the website / app");
    }

    private static async Task<IResult> List([AsParameters] GroupQuery q, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Groups.AsNoTracking();
        if (!q.IncludeInactive) query = query.Where(g => g.IsActive);
        if (!string.IsNullOrWhiteSpace(q.Search)) query = query.Where(g => EF.Functions.ILike(g.Name, $"%{q.Search.Trim()}%"));
        if (q.Type is { } type) query = query.Where(g => g.Type == type);
        if (q.BranchId is { } branchId) query = query.Where(g => g.BranchId == branchId);
        if (q.ParentGroupId is { } parentId) query = query.Where(g => g.ParentGroupId == parentId);

        var total = await query.LongCountAsync(ct);
        var groups = await query.OrderBy(g => g.Name).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var items = await ToResponsesAsync(db, people, groups, ct);
        return Results.Ok(new PagedResult<GroupResponse>(items, page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Get(Guid id, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var entity = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        return entity is null ? NotFound.ToProblem() : Results.Ok((await ToResponsesAsync(db, people, [entity], ct))[0]);
    }

    private static async Task<IResult> Create(SaveGroupRequest r, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var slug = r.Slug ?? Slug.From(r.Name);
        if (await db.Groups.AnyAsync(g => g.Slug == slug, ct))
        {
            return SlugTaken.ToProblem();
        }

        var entity = Group.Create(r.Name, slug, r.Type);
        Apply(entity, r, slug);
        db.Groups.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/groups/{entity.Id}", (await ToResponsesAsync(db, people, [entity], ct))[0]);
    }

    private static async Task<IResult> Update(Guid id, SaveGroupRequest r, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        var slug = r.Slug ?? entity.Slug;
        if (await db.Groups.AnyAsync(g => g.Slug == slug && g.Id != id, ct))
        {
            return SlugTaken.ToProblem();
        }

        if (r.ParentGroupId is { } parentId && await CreatesCycleAsync(db, id, parentId, ct))
        {
            return Error.Validation("group.cycle", "A group cannot be nested inside one of its own sub-groups.").ToProblem();
        }

        Apply(entity, r, slug);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToResponsesAsync(db, people, [entity], ct))[0]);
    }

    private static void Apply(Group entity, SaveGroupRequest r, string slug) =>
        entity.Update(r.Name, slug, r.Type, r.Visibility, r.ParentGroupId, r.BranchId, r.Description, r.ImageUrl, r.MeetingSchedule,
            r.MeetingLocation, r.Capacity, r.IsActive, r.AcceptsJoinRequests);

    private static async Task<bool> CreatesCycleAsync(GroupsDbContext db, Guid groupId, Guid parentId, CancellationToken ct)
    {
        var current = (Guid?)parentId;
        for (var depth = 0; current is not null && depth < 20; depth++)
        {
            if (current == groupId)
            {
                return true;
            }

            current = await db.Groups.Where(g => g.Id == current).Select(g => g.ParentGroupId).FirstOrDefaultAsync(ct);
        }

        return false;
    }

    private static async Task<IResult> Delete(Guid id, GroupsDbContext db, CancellationToken ct)
    {
        var entity = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        if (await db.Groups.AnyAsync(g => g.ParentGroupId == id, ct))
        {
            return Error.Conflict("group.has_children", "Move or archive the sub-groups first.").ToProblem();
        }

        db.Groups.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListMembers(Guid id, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var members = await db.Members.AsNoTracking().Where(m => m.GroupId == id).ToListAsync(ct);
        var summaries = await people.GetSummariesAsync(members.Select(m => m.PersonId), ct);

        return Results.Ok(members
            .Where(m => summaries.ContainsKey(m.PersonId))
            .Select(m => (m, p: summaries[m.PersonId]))
            .OrderBy(x => x.m.Role).ThenBy(x => x.p.FullName)
            .Select(x => new GroupMemberResponse(x.m.PersonId, x.p.FullName, x.p.Email, x.p.PhoneNumber, x.p.PhotoUrl,
                x.m.Role.ToString(), x.m.Status.ToString(), x.m.JoinedOn)));
    }

    private static async Task<IResult> AddMember(Guid id, AddGroupMemberRequest r, GroupsDbContext db, IPeopleDirectory people, TimeProvider clock, CancellationToken ct)
    {
        var entity = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        if ((await people.GetSummariesAsync([r.PersonId], ct)).Count == 0)
        {
            return Error.NotFound("person.not_found", "The person was not found.").ToProblem();
        }

        entity.AddMember(r.PersonId, r.Role, r.Status, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMember(Guid id, Guid personId, GroupsDbContext db, CancellationToken ct)
    {
        var entity = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (entity is null)
        {
            return NotFound.ToProblem();
        }

        entity.RemoveMember(personId);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ByPerson(Guid personId, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var groupIds = db.Members.Where(m => m.PersonId == personId).Select(m => m.GroupId);
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Name).ToListAsync(ct);
        return Results.Ok(await ToResponsesAsync(db, people, groups, ct));
    }

    private static async Task<IResult> MyGroups(ICurrentUser user, GroupsDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        if (await people.FindPersonIdByUserAsync(user.RequiredUserId, ct) is not { } personId)
        {
            return Results.Ok(Array.Empty<GroupResponse>());
        }

        return await ByPerson(personId, db, people, ct);
    }

    private static async Task<IResult> PublicList(HttpContext http, GroupsDbContext db, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(await db.Groups.AsNoTracking()
            .Where(g => g.IsActive && g.Visibility == GroupVisibility.Public)
            .OrderBy(g => g.Name)
            .Select(g => new PublicGroupResponse(g.Id, g.Name, g.Slug, g.Type.ToString(), g.Description, g.ImageUrl, g.MeetingSchedule,
                g.MeetingLocation, g.AcceptsJoinRequests))
            .ToListAsync(ct));
    }

    private static async Task<List<GroupResponse>> ToResponsesAsync(GroupsDbContext db, IPeopleDirectory people, IReadOnlyList<Group> groups, CancellationToken ct)
    {
        var ids = groups.Select(g => g.Id).ToList();
        var counts = await db.Members.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId) && m.Status == GroupMemberStatus.Active)
            .GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var leaders = await db.Members.AsNoTracking()
            .Where(m => ids.Contains(m.GroupId) && (m.Role == GroupMemberRole.Leader || m.Role == GroupMemberRole.AssistantLeader))
            .ToListAsync(ct);
        var summaries = await people.GetSummariesAsync(leaders.Select(l => l.PersonId), ct);

        return groups.Select(g => new GroupResponse(
            g.Id, g.Name, g.Slug, g.Type.ToString(), g.Visibility.ToString(), g.ParentGroupId, g.BranchId, g.Description, g.ImageUrl,
            g.MeetingSchedule, g.MeetingLocation, g.Capacity, g.IsActive, g.AcceptsJoinRequests, counts.GetValueOrDefault(g.Id),
            leaders.Where(l => l.GroupId == g.Id && summaries.ContainsKey(l.PersonId))
                .Select(l => new GroupLeaderResponse(l.PersonId, summaries[l.PersonId].FullName, l.Role.ToString(), summaries[l.PersonId].PhotoUrl))
                .ToList())).ToList();
    }
}
