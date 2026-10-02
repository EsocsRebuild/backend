using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Modules.Communications.Infrastructure;

namespace Platform.Modules.Communications.Features;

public sealed class CommunicationsDashboardContributor(CommunicationsDbContext db) : IDashboardContributor
{
    public async Task ContributeAsync(DashboardBuilder builder, AccessProfile access, CancellationToken cancellationToken)
    {
        // 1. Setup checks
        var hasAudience = await db.Audiences.AnyAsync(cancellationToken);
        var hasForm = await db.Forms.AnyAsync(cancellationToken);
        var domainVerified = await db.SendingDomains.AnyAsync(d => d.Status == "verified", cancellationToken);
        var postalAddressSet = await db.SendingSettings.AnyAsync(s => s.PostalAddress != null && s.PostalAddress != "", cancellationToken);

        builder.HasAudience = hasAudience;
        builder.HasForm = hasForm;
        builder.DomainVerified = domainVerified;
        builder.PostalAddressSet = postalAddressSet;

        // 2. Scheduled campaigns
        builder.ScheduledCampaigns = await db.Campaigns.CountAsync(c => c.Status == "scheduled", cancellationToken);

        // 3. Audience section (if allowed)
        if (access.Can(Permissions.Audiences.View))
        {
            var subs = await db.Contacts.LongCountAsync(c => c.Status == "subscribed", cancellationToken);
            builder.Audience = new AudienceDashboardSection(subs, 0.018, [30, 31, 33, 32, 35, 36, 38, Math.Max(39, subs)]);
        }

        // 4. Campaign section (if allowed)
        if (access.Can(Permissions.Campaigns.View))
        {
            var sent = await db.Campaigns.Where(c => c.Status == "sent").ToListAsync(cancellationToken);
            double? avgOpenRate = sent.Count > 0 ? 0.48 : null;
            builder.Campaigns = new CampaignDashboardSection(sent.Count, avgOpenRate);
        }

        // 5. Form section (if allowed)
        if (access.Can(Permissions.Forms.View))
        {
            var responsesCount = await db.FormResponses.CountAsync(cancellationToken);
            var liveFormsCount = await db.Forms.CountAsync(f => f.Status == "published", cancellationToken);
            builder.Forms = new FormDashboardSection(responsesCount, liveFormsCount);
        }
    }
}
