using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;

namespace Platform.Modules.People.Features;

/// <summary>
/// When an account is created in an organisation, link it to the matching person (same email, no account yet).
/// Website / app member accounts without a match get a new profile, awaiting approval; staff accounts are
/// only linked (an administrator isn't necessarily a member). Idempotent.
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
            if (e.Kind != "Member")
            {
                return;
            }

            var names = e.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var (first, last) = (names.ElementAtOrDefault(0) ?? e.Name, names.ElementAtOrDefault(1) ?? string.Empty);
            person = await factory.CreateAsync(first, last, MembershipStatus.Visitor, cancellationToken);
            person.UpdateProfile(new PersonProfile(
                null, first, null, last, null, Gender.Unspecified, null, MaritalStatus.Unspecified, null, email,
                e.Phone, null, null, null, null, null, "website", false, null, null, null, null, null));
            person.SetStatus(RecordStatus.Pending);
            db.People.Add(person);
        }

        person.LinkAccount(e.UserId, e.MembershipId);
        await db.SaveChangesAsync(cancellationToken);
    }
}
