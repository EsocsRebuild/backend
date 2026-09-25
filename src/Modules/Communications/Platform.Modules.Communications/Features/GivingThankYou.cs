using System.Globalization;
using System.Net;
using Platform.Application.Abstractions;
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
        var organisation = tenant?.Name ?? "our church";
        PersonSummary? person = null;
        if (e.PersonId is { } personId)
        {
            person = (await people.GetSummariesAsync([personId], cancellationToken)).GetValueOrDefault(personId);
        }

        var amount = e.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + e.Currency;
        var name = person?.FullName.Split(' ')[0] ?? e.DonorName?.Split(' ')[0] ?? "friend";
        var to = e.DonorEmail ?? person?.Email;

        if (!string.IsNullOrWhiteSpace(to))
        {
            var html = $"""
                <p>Dear {WebUtility.HtmlEncode(name)},</p>
                <p>Thank you for your generous gift of <strong>{amount}</strong> to {WebUtility.HtmlEncode(organisation)} on {e.ReceivedOn:dd MMM yyyy}.</p>
                <p>Receipt number: <strong>{e.ReceiptNumber}</strong></p>
                <p>"God loves a cheerful giver." — 2 Corinthians 9:7</p>
                """;
            await email.SendAsync(new EmailMessage(to, $"Thank you for your gift — receipt {e.ReceiptNumber}", html,
                $"Dear {name},\n\nThank you for your gift of {amount} to {organisation} on {e.ReceivedOn:dd MMM yyyy}.\nReceipt: {e.ReceiptNumber}"), cancellationToken);
        }

        if (person?.UserId is { } userId)
        {
            db.Notifications.Add(Notification.Create(userId, "giving", "Thank you for your gift",
                $"We received {amount}. Receipt {e.ReceiptNumber}.", "/giving"));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
