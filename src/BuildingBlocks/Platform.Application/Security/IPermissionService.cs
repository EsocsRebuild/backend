namespace Platform.Application.Security;

/// <summary>What the caller may do in the current organisation.</summary>
public sealed record AccessProfile(
    Guid MembershipId,
    IReadOnlySet<string> Permissions,
    /// <summary>Unit (parish) the caller is limited to, or null for organisation-wide access.</summary>
    Guid? ScopeUnitId,
    bool IsStaff)
{
    public bool Can(string permission) => Permissions.Contains(permission);
}

/// <summary>Resolves effective permissions and scope for a user within a tenant (cached).</summary>
public interface IPermissionService
{
    Task<AccessProfile?> GetAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);

    Task InvalidateAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>The current caller's <see cref="AccessProfile"/>, memoised for the request.</summary>
public interface ICurrentAccess
{
    Task<AccessProfile?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Checks the <c>X-Sudo-Token</c> header against the caller's session (recent password confirmation).</summary>
public interface ISudoVerifier
{
    Task<bool> IsValidAsync(string token, CancellationToken cancellationToken);
}
