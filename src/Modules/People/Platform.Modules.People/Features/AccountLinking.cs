using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;

namespace Platform.Modules.People.Features;

/// <summary>
/// When an account is created in a tenant, link it to the matching person (same email, no account yet)
/// or create a new person profile. Idempotent.
/// </summary>
internal sealed class AccountLinking(PeopleDbContext db, PersonFactory factory) : IEventHandler<MemberAccountCreatedIntegrationEvent>
{
    public async Task Handle(MemberAccountCreatedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (await db.People.AnyAsync(p => p.UserId == e.UserId, cancellationToken))
        {
            return;
        }

        var email = e.Email.ToLowerInvariant();
        var person = await db.People
            .Where(p => p.UserId == null && p.Email == email)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (person is null)
        {
            person = await factory.CreateAsync(e.FirstName, e.LastName, MembershipStatus.Visitor, cancellationToken);
            person.UpdateProfile(new PersonProfile(
                null, e.FirstName, null, e.LastName, null, Gender.Unspecified, null, MaritalStatus.Unspecified, null, email,
                e.PhoneNumber, null, null, null, null, null, "app-registration", false, null, null, null, null, null));
            db.People.Add(person);
        }

        person.LinkAccount(e.UserId, e.MembershipId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
