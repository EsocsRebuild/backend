using Microsoft.AspNetCore.SignalR;
using Platform.Api.Hubs;
using Platform.Application.RealTime;

namespace Platform.Api.RealTime;

/// <summary>
/// Bridge that publishes real-time domain events through ASP.NET Core SignalR hub groups.
/// </summary>
public sealed class SignalRChurchRealTimeNotifier(
    IHubContext<ChurchPlatformHub, IChurchPlatformClient> hubContext) : IChurchRealTimeNotifier
{
    public Task BroadcastCampaignProgressAsync(
        Guid campaignId,
        decimal totalRaised,
        decimal goalAmount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        var group = ChurchPlatformHub.CampaignRoom(campaignId);
        return hubContext.Clients.Group(group)
            .ReceiveCampaignProgress(campaignId, totalRaised, goalAmount, currency);
    }

    public Task BroadcastPrayerUpdateAsync(
        Guid prayerRequestId,
        string title,
        string? content,
        string authorName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        return hubContext.Clients.Group(ChurchPlatformHub.PrayerWallGlobalRoom)
            .ReceivePrayerUpdate(prayerRequestId, title, content, authorName, createdAt);
    }
}
