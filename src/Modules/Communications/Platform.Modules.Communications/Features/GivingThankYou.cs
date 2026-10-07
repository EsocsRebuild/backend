using System.Globalization;
using Platform.Application.Abstractions;
using Platform.Application.Emails;
using Platform.Application.Messaging;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.Giving.Contracts;
using Platform.Modules.People.Contracts;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Communications.Features;

/// <summary>
/// Thanks the giver when a gift completes: an email receipt (when an address is known) and an in-app
/// notification for members with an account. Duplicate delivery is harmless (a second receipt at worst).
/// </summary>
internal sealed class GivingThankYou(
    IEmailSender email, IPeopleDirectory people, ITenantDirectory tenants, CommunicationsDbContext db)
    : IEventHandler<DonationCompletedIntegrationEvent>
{
    public async Task Handle(DonationCompletedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(e.TenantId, cancellationToken);
        var organisation = tenant?.Name ?? ChurchEmailLayoutRenderer.DefaultChurchName;
        PersonSummary? person = null;
        if (e.PersonId is { } personId)
        {
            person = (await people.GetSummariesAsync([personId], cancellationToken)).GetValueOrDefault(personId);
        }

        var donorFullName = person?.FullName ?? e.DonorName ?? "Faithful Giver";
        var to = e.DonorEmail ?? person?.Email;

        if (!string.IsNullOrWhiteSpace(to))
        {
            var msg = ChurchEmailLayoutRenderer.BuildDonationReceiptMessage(
                to: to,
                donorName: donorFullName,
                churchName: organisation,
                fundName: "General & Kingdom Initiatives",
                amount: e.Amount,
                currency: e.Currency,
                reference: e.ReceiptNumber,
                date: new DateTimeOffset(e.ReceivedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

            await email.SendAsync(msg, cancellationToken);
        }

        if (person?.UserId is { } userId)
        {
            var formattedAmount = $"{e.Currency.ToUpperInvariant()} {e.Amount.ToString("N2", CultureInfo.InvariantCulture)}";
            db.Notifications.Add(Notification.Create(userId, "giving", "Thank you for your gift",
                $"We received {formattedAmount}. Official Receipt: {e.ReceiptNumber}.", "/giving"));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
