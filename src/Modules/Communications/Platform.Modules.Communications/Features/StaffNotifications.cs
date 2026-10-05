using Platform.Application.Messaging;
using Platform.Application.Security;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.Identity.Contracts;

namespace Platform.Modules.Communications.Features;

/// <summary>Tells administrators who manage users (and want to hear about it) that someone asked for access.</summary>
internal sealed class AccessRequestNotifications(CommunicationsDbContext db, IUserDirectory users) : IEventHandler<AccessRequestSubmittedIntegrationEvent>
{
    public async Task Handle(AccessRequestSubmittedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var recipients = await users.GetStaffUserIdsWithPermissionAsync(Permissions.Users.Manage, "accessRequests", cancellationToken);
        foreach (var userId in recipients)
        {
            db.Notifications.Add(Notification.Create(userId, "access_request", $"{e.Name} requested access", e.Email, "/users/requests",
                NotificationTone.Warning));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
