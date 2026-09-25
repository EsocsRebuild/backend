using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Security;
using Platform.Modules.People.Infrastructure;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;

namespace Platform.Modules.People.Features;

public sealed record UpdateMyProfileRequest(string? PhoneNumber, string? AlternatePhoneNumber, Address? Address, string? Occupation, string? PhotoUrl, bool ConsentToContact);

internal sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.PhoneNumber).MaximumLength(32);
        RuleFor(x => x.AlternatePhoneNumber).MaximumLength(32);
        RuleFor(x => x.Occupation).MaximumLength(100);
        RuleFor(x => x.PhotoUrl).MaximumLength(1024);
    }
}

/// <summary>
/// Self-service profile for members in the mobile app / website. Requires only authentication:
/// members can read and edit their own contact details, never other people's.
/// </summary>
public static class MyProfileEndpoints
{
    private static readonly Error NoProfile = Error.NotFound("person.no_profile", "No member profile is linked to your account yet.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup($"{EndpointExtensions.ApiPrefix}/me/profile").WithTags("My account").RequireAuthorization();

        group.MapGet("/", async (ICurrentUser user, PeopleDbContext db, CancellationToken ct) =>
                await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.UserId, ct) is { } person
                    ? Results.Ok(person.ToResponse())
                    : NoProfile.ToProblem())
            .WithSummary("My member profile");

        group.MapPut("/", async (UpdateMyProfileRequest r, ICurrentUser user, PeopleDbContext db, CancellationToken ct) =>
            {
                var person = await db.People.FirstOrDefaultAsync(p => p.UserId == user.UserId, ct);
                if (person is null)
                {
                    return NoProfile.ToProblem();
                }

                person.UpdateContactDetails(r.PhoneNumber, r.AlternatePhoneNumber, r.Address, r.Occupation, r.PhotoUrl, r.ConsentToContact);
                await db.SaveChangesAsync(ct);
                return Results.Ok(person.ToResponse());
            })
            .WithValidation<UpdateMyProfileRequest>()
            .WithSummary("Update my contact details");

        group.MapGet("/household", async (ICurrentUser user, PeopleDbContext db, CancellationToken ct) =>
            {
                var householdId = await db.People.Where(p => p.UserId == user.UserId).Select(p => p.HouseholdId).FirstOrDefaultAsync(ct);
                if (householdId is null)
                {
                    return Results.Ok(Array.Empty<HouseholdMemberResponse>());
                }

                return Results.Ok(await db.People.AsNoTracking().Where(p => p.HouseholdId == householdId)
                    .Select(p => new HouseholdMemberResponse(p.Id, (p.PreferredName ?? p.FirstName) + " " + p.LastName,
                        p.HouseholdRole.HasValue ? p.HouseholdRole.Value.ToString() : null, p.PhotoUrl, p.DateOfBirth))
                    .ToListAsync(ct));
            })
            .WithSummary("My household (for family check-in and giving)");
    }
}
