using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Platform.Application.Security;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Enums;

namespace Platform.Api.Hubs;

/// <summary>
/// Central real-time communication hub supporting ecclesiastical rooms, live streams, prayer wall, and financial campaigns.
/// Secured with tenant and ecclesiastical unit scope validation.
/// </summary>
public sealed class ChurchPlatformHub(ICurrentAccess access, IUnitDirectory units) : Hub<IChurchPlatformClient>
{
    public static string EcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId) =>
        $"wing:{wing.ToString().ToLowerInvariant()}:tier:{tier.ToString().ToLowerInvariant()}:{unitId:D}";

    public static string LiveServiceRoom(Guid branchId) => $"service:stream:{branchId:D}";

    public const string PrayerWallGlobalRoom = "prayer:wall:global";

    public static string CampaignRoom(Guid campaignId) => $"finance:campaign:{campaignId:D}";

    [Authorize]
    public async Task JoinEcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId)
    {
        var profile = await access.GetAsync(Context.ConnectionAborted);
        if (profile is null)
        {
            throw new HubException("Unauthorized to join ecclesiastical room.");
        }

        if (profile.ScopeUnitId is { } scopeUnit)
        {
            var allowed = await units.GetSubtreeIdsAsync(scopeUnit, Context.ConnectionAborted);
            if (!allowed.Contains(unitId))
            {
                throw new HubException("Forbidden: Target unit is outside your ecclesiastical scope.");
            }
        }

        var group = EcclesiasticalRoom(wing, tier, unitId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    [Authorize]
    public async Task LeaveEcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId)
    {
        var group = EcclesiasticalRoom(wing, tier, unitId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    [AllowAnonymous]
    public async Task JoinLiveService(Guid branchId)
    {
        var group = LiveServiceRoom(branchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    [AllowAnonymous]
    public async Task LeaveLiveService(Guid branchId)
    {
        var group = LiveServiceRoom(branchId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    [AllowAnonymous]
    public async Task JoinPrayerWall()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, PrayerWallGlobalRoom);
    }

    [AllowAnonymous]
    public async Task LeavePrayerWall()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PrayerWallGlobalRoom);
    }

    public async Task JoinCampaignRoom(Guid campaignId)
    {
        var group = CampaignRoom(campaignId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveCampaignRoom(Guid campaignId)
    {
        var group = CampaignRoom(campaignId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }
}

