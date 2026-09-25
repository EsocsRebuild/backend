using Platform.SharedKernel.Domain;

namespace Platform.Modules.Groups.Domain;

public enum GroupType
{
    Ministry,
    Department,
    CellGroup,
    SmallGroup,
    Choir,
    Committee,
    Class,
    Team,
    Other,
}

public enum GroupVisibility
{
    /// <summary>Listed on the website / app; people can request to join.</summary>
    Public,

    /// <summary>Visible to signed-in members only.</summary>
    Members,

    /// <summary>Staff only.</summary>
    Private,
}

public enum GroupMemberRole
{
    Leader,
    AssistantLeader,
    Secretary,
    Member,
}

public enum GroupMemberStatus
{
    Pending,
    Active,
    Inactive,
}

/// <summary>
/// Any ministry, department, cell/home group, choir, class or committee. Groups can nest
/// (e.g. Music Department → Choir → Sopranos) via <see cref="ParentGroupId"/>.
/// </summary>
public sealed class Group : TenantAggregateRoot
{
    private readonly List<GroupMember> _members = [];

    private Group() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public GroupType Type { get; private set; }
    public GroupVisibility Visibility { get; private set; }
    public Guid? ParentGroupId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }

    /// <summary>Human description of the regular meeting, e.g. "Wednesdays 6pm".</summary>
    public string? MeetingSchedule { get; private set; }

    public string? MeetingLocation { get; private set; }
    public int? Capacity { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool AcceptsJoinRequests { get; private set; }

    public IReadOnlyCollection<GroupMember> Members => _members.AsReadOnly();

    public static Group Create(string name, string slug, GroupType type) =>
        new() { Name = name.Trim(), Slug = slug, Type = type, Visibility = GroupVisibility.Members };

    public void Update(
        string name, string slug, GroupType type, GroupVisibility visibility, Guid? parentGroupId, Guid? branchId, string? description,
        string? imageUrl, string? meetingSchedule, string? meetingLocation, int? capacity, bool isActive, bool acceptsJoinRequests)
    {
        if (parentGroupId == Id)
        {
            throw new DomainException("A group cannot be its own parent.");
        }

        Name = name.Trim();
        Slug = slug;
        Type = type;
        Visibility = visibility;
        ParentGroupId = parentGroupId;
        BranchId = branchId;
        Description = description;
        ImageUrl = imageUrl;
        MeetingSchedule = meetingSchedule;
        MeetingLocation = meetingLocation;
        Capacity = capacity;
        IsActive = isActive;
        AcceptsJoinRequests = acceptsJoinRequests;
    }

    public GroupMember AddMember(Guid personId, GroupMemberRole role, GroupMemberStatus status, DateOnly joinedOn)
    {
        var existing = _members.FirstOrDefault(m => m.PersonId == personId);
        if (existing is not null)
        {
            existing.Change(role, status);
            return existing;
        }

        if (Capacity is { } cap && status == GroupMemberStatus.Active && _members.Count(m => m.Status == GroupMemberStatus.Active) >= cap)
        {
            throw new DomainException("The group is full.");
        }

        var member = new GroupMember(Id, personId, role, status, joinedOn);
        _members.Add(member);
        return member;
    }

    public void RemoveMember(Guid personId) => _members.RemoveAll(m => m.PersonId == personId);
}

public sealed class GroupMember : TenantEntity
{
    private GroupMember() { }

    internal GroupMember(Guid groupId, Guid personId, GroupMemberRole role, GroupMemberStatus status, DateOnly joinedOn)
    {
        GroupId = groupId;
        PersonId = personId;
        Role = role;
        Status = status;
        JoinedOn = joinedOn;
    }

    public Guid GroupId { get; private set; }

    /// <summary>People module person id (reference by id only).</summary>
    public Guid PersonId { get; private set; }

    public GroupMemberRole Role { get; private set; }
    public GroupMemberStatus Status { get; private set; }
    public DateOnly JoinedOn { get; private set; }

    internal void Change(GroupMemberRole role, GroupMemberStatus status)
    {
        Role = role;
        Status = status;
    }
}
