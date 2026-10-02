using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Platform.Application.Security;
using Platform.Application.Tenancy;

namespace Platform.Web.Security;

/// <summary>Requires that the caller holds ALL listed permissions in the current tenant.</summary>
public sealed class PermissionRequirement(IReadOnlyCollection<string> permissions) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> Permissions { get; } = permissions;
}

internal sealed class PermissionAuthorizationHandler(ICurrentAccess access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        // API keys carry their granted scopes directly on the principal.
        var scopes = context.User.FindAll(PlatformClaims.Scope).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        if (scopes.Count > 0)
        {
            if (requirement.Permissions.All(scopes.Contains))
            {
                context.Succeed(requirement);
            }

            return;
        }

        var profile = await access.GetAsync(CancellationToken.None);
        if (profile is not null && requirement.Permissions.All(profile.Permissions.Contains))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Per-request memo of the caller's access profile (permissions + parish scope).</summary>
internal sealed class CurrentAccess(IPermissionService permissions, ICurrentUser user, ITenantContext tenant) : ICurrentAccess
{
    private Task<AccessProfile?>? _profile;

    public Task<AccessProfile?> GetAsync(CancellationToken cancellationToken) =>
        _profile ??= user.UserId is { } userId && tenant.TenantId is { } tenantId
            ? permissions.GetAccessAsync(userId, tenantId, cancellationToken)
            : Task.FromResult<AccessProfile?>(null);
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        foreach (var permission in permissions)
        {
            if (!Permissions.IsKnown(permission))
            {
                throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permissions));
            }
        }

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();

        return builder.RequireAuthorization(policy);
    }
}
