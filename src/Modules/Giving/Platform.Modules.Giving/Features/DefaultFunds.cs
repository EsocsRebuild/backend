using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Domain;
using Platform.Modules.Giving.Infrastructure;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Giving.Features;

/// <summary>Every new organisation starts with the standard church funds. Idempotent.</summary>
internal sealed class DefaultFunds(GivingDbContext db) : IEventHandler<TenantCreatedIntegrationEvent>
{
    private static readonly (string Name, string Code, string Description)[] Defaults =
    [
        ("Tithe", "TITHE", "Tithes (10% of income)."),
        ("Offering", "OFFERING", "General offerings."),
        ("Building Fund", "BUILDING", "Church building and facilities projects."),
        ("Missions", "MISSIONS", "Missions and evangelism."),
        ("Welfare", "WELFARE", "Support for members and the community in need."),
        ("Thanksgiving", "THANKS", "Thanksgiving offerings."),
    ];

    public async Task Handle(TenantCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (await db.Funds.IgnoreQueryFilters([QueryFilters.Tenant]).AnyAsync(f => f.TenantId == e.TenantId, cancellationToken))
        {
            return;
        }

        for (var i = 0; i < Defaults.Length; i++)
        {
            var fund = Fund.Create(Defaults[i].Name, Defaults[i].Code, Defaults[i].Description, isPublic: true, sortOrder: i);
            fund.AssignTenant(e.TenantId);
            db.Funds.Add(fund);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
