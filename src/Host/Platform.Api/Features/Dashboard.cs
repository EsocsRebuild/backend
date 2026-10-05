using Platform.Application.Abstractions;
using Platform.Application.Security;
using Platform.Modules.Identity.Contracts;
using Platform.Modules.People.Contracts;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Api.Features;

public sealed record MemberSection(long Total, long NewThisMonth, double? Delta, IReadOnlyList<long> Trend);

public sealed record PendingSection(int AccessRequests, long MemberApprovals, int ScheduledCampaigns);

public sealed record SetupSection(bool DomainVerified, bool HasAudience, bool HasForm, bool PostalAddressSet);

public sealed record DashboardSummary(MemberSection? Members, AudienceDashboardSection? Audience, CampaignDashboardSection? Campaigns, FormDashboardSection? Forms,
    PendingSection Pending, SetupSection Setup);


/// <summary>
/// <c>GET /dashboard/summary</c> (API contract §6). Composed from each module's read-only contracts;
/// sections the caller has no permission for are <c>null</c>.
/// </summary>
internal static class Dashboard
{
    public static void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapModuleGroup("dashboard", "Dashboard")
            .MapGet("/summary", Summary)
            .RequirePermission(Permissions.Dashboard.View)
            .WithSummary("Headline figures, pending work and setup checklist");

    private static async Task<IResult> Summary(ICurrentAccess currentAccess, IPeopleDirectory people, IUserDirectory users,
        IEnumerable<IDashboardContributor> contributors, CancellationToken ct)
    {
        var access = (await currentAccess.GetAsync(ct))!;

        MemberSection? members = null;
        long memberApprovals = 0;
        if (access.Can(Permissions.Members.View))
        {
            var stats = await people.GetDashboardStatsAsync(ct);
            members = new MemberSection(stats.Total, stats.NewThisMonth,
                stats.NewLastMonth == 0 ? null : Math.Round((stats.NewThisMonth - stats.NewLastMonth) / (double)stats.NewLastMonth, 3), stats.Trend);
            memberApprovals = stats.PendingApproval;
        }

        var builder = new DashboardBuilder();
        foreach (var contributor in contributors)
        {
            await contributor.ContributeAsync(builder, access, ct);
        }

        var accessRequests = access.Can(Permissions.Users.Manage) ? await users.CountPendingAccessRequestsAsync(ct) : 0;
        return Results.Ok(new DashboardSummary(members, builder.Audience, builder.Campaigns, builder.Forms,
            new PendingSection(accessRequests, memberApprovals, builder.ScheduledCampaigns),
            new SetupSection(builder.DomainVerified, builder.HasAudience, builder.HasForm, builder.PostalAddressSet)));
    }
}
