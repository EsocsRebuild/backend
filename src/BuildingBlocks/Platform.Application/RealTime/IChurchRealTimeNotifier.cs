namespace Platform.Application.RealTime;

/// <summary>
/// Dispatches real-time events to connected clients via WebSockets / SignalR.
/// </summary>
public interface IChurchRealTimeNotifier
{
    Task BroadcastCampaignProgressAsync(
        Guid campaignId,
        decimal totalRaised,
        decimal goalAmount,
        string currency,
        CancellationToken cancellationToken = default);

    Task BroadcastPrayerUpdateAsync(
        Guid prayerRequestId,
        string title,
        string? content,
        string authorName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);
}
