namespace Platform.Modules.Identity.Contracts;

public sealed record UserSummary(Guid Id, string Name, string Email);

/// <summary>Read-only lookup of account names (e.g. "created by" columns in other modules).</summary>
public interface IUserDirectory
{
    /// <summary>Accepts user ids or staff membership ids; returns what it finds, keyed by the id given.</summary>
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Active staff (in the current organisation) holding <paramref name="permission"/> who want this kind of notification:
    /// <c>accessRequests</c>, <c>formResponses</c>, <c>campaignReports</c> or <c>weeklySummary</c> (null = everyone).
    /// </summary>
    Task<IReadOnlyList<Guid>> GetStaffUserIdsWithPermissionAsync(string permission, string? preference, CancellationToken cancellationToken);

    /// <summary>Access requests with a verified email, waiting for a decision.</summary>
    Task<int> CountPendingAccessRequestsAsync(CancellationToken cancellationToken);
}
