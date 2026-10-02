using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Platform.SharedKernel.Enums;

namespace Platform.Api.Hubs;

/// <summary>
/// Central real-time communication hub supporting ecclesiastical rooms, live streams, prayer wall, and financial campaigns.
/// </summary>
public sealed class ChurchPlatformHub : Hub<IChurchPlatformClient>
{
    public static string EcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId) =>
        $"wing:{wing.ToString().ToLowerInvariant()}:tier:{tier.ToString().ToLowerInvariant()}:{unitId:D}";

    public static string LiveServiceRoom(Guid branchId) => $"service:stream:{branchId:D}";

    public const string PrayerWallGlobalRoom = "prayer:wall:global";

    public static string CampaignRoom(Guid campaignId) => $"finance:campaign:{campaignId:D}";

    public async Task JoinEcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId)
    {
        var group = EcclesiasticalRoom(wing, tier, unitId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveEcclesiasticalRoom(OrgWing wing, OrgTier tier, Guid unitId)
    {
        var group = EcclesiasticalRoom(wing, tier, unitId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public async Task JoinLiveService(Guid branchId)
    {
        var group = LiveServiceRoom(branchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task LeaveLiveService(Guid branchId)
    {
        var group = LiveServiceRoom(branchId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }

    public async Task JoinPrayerWall()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, PrayerWallGlobalRoom);
    }

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
