namespace Platform.Modules.Groups.Contracts;

/// <summary>Read-only public API of the Groups module.</summary>
public interface IGroupDirectory
{
    /// <summary>Active members of a group (optionally including its sub-groups).</summary>
    Task<IReadOnlyList<Guid>> GetMemberPersonIdsAsync(Guid groupId, bool includeSubGroups, CancellationToken cancellationToken);

    /// <summary>Groups a person actively belongs to.</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsForPersonAsync(Guid personId, CancellationToken cancellationToken);
}
