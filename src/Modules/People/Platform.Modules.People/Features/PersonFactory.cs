using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;

namespace Platform.Modules.People.Features;

/// <summary>Creates people with a unique, sequential member number such as <c>M-000042</c>.</summary>
internal sealed class PersonFactory(PeopleDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<Person> CreateAsync(string firstName, string lastName, MembershipStatus status, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var number = await db.NextNumberAsync(tenantId, "member", ct);
        return Person.Create(tenantId, $"M-{number:D6}", firstName, lastName, status, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}
