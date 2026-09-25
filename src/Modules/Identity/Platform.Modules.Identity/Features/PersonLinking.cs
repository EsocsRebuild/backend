using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Identity.Infrastructure;
using Platform.Modules.People.Contracts;

namespace Platform.Modules.Identity.Features;

/// <summary>Stores the People module's person id on the membership (for "my profile" and giving history).</summary>
internal sealed class PersonLinking(IdentityDbContext db) : IEventHandler<PersonLinkedToAccountIntegrationEvent>
{
    public async Task Handle(PersonLinkedToAccountIntegrationEvent e, CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(m => m.Id == e.MembershipId && m.TenantId == e.TenantId, cancellationToken);

        if (membership is null || membership.PersonId == e.PersonId)
        {
            return;
        }

        membership.LinkPerson(e.PersonId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
