namespace Platform.Api.Hubs;

/// <summary>
/// Strongly-typed SignalR client methods for real-time ecclesiastical, financial, and prayer updates.
/// </summary>
public interface IChurchPlatformClient
{
    Task ReceiveCampaignProgress(Guid campaignId, decimal totalRaised, decimal goalAmount, string currency);

    Task ReceivePrayerUpdate(Guid prayerRequestId, string title, string? content, string authorName, DateTimeOffset createdAt);

    Task ReceiveEcclesiasticalNotification(string channel, object payload);

    Task ReceiveServiceEvent(Guid branchId, string eventType, object payload);
}
