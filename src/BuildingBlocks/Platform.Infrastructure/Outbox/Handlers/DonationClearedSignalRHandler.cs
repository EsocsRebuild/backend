using MediatR;
using Microsoft.Extensions.Logging;
using Platform.Application.RealTime;
using Platform.Modules.Giving.Contracts;

namespace Platform.Infrastructure.Outbox.Handlers;

/// <summary>
/// Consumes <see cref="DonationClearedIntegrationEvent"/> and streams progress to campaign rooms via SignalR.
/// </summary>
public sealed class DonationClearedSignalRHandler(
    IChurchRealTimeNotifier realTimeNotifier,
    ILogger<DonationClearedSignalRHandler> logger) : INotificationHandler<DonationClearedIntegrationEvent>
{
    public async Task Handle(DonationClearedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Broadcasting campaign progress for Campaign {CampaignId}: {Raised}/{Goal} {Currency}",
            notification.CampaignId, notification.TotalRaised, notification.GoalAmount, notification.Currency);

        await realTimeNotifier.BroadcastCampaignProgressAsync(
            notification.CampaignId,
            notification.TotalRaised,
            notification.GoalAmount,
            notification.Currency,
            cancellationToken);
    }
}
