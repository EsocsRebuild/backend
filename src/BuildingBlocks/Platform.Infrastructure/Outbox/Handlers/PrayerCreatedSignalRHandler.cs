using MediatR;
using Microsoft.Extensions.Logging;
using Platform.Application.RealTime;
using Platform.Modules.Communications.Contracts;

namespace Platform.Infrastructure.Outbox.Handlers;

/// <summary>
/// Consumes <see cref="PrayerCreatedIntegrationEvent"/> and streams new prayer requests to the global prayer wall.
/// </summary>
public sealed class PrayerCreatedSignalRHandler(
    IChurchRealTimeNotifier realTimeNotifier,
    ILogger<PrayerCreatedSignalRHandler> logger) : INotificationHandler<PrayerCreatedIntegrationEvent>
{
    public async Task Handle(PrayerCreatedIntegrationEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Broadcasting prayer wall update for Prayer {PrayerRequestId} by {Author}",
            notification.PrayerRequestId, notification.AuthorName);

        await realTimeNotifier.BroadcastPrayerUpdateAsync(
            notification.PrayerRequestId,
            notification.Title,
            notification.Content,
            notification.AuthorName,
            notification.CreatedAt,
            cancellationToken);
    }
}
