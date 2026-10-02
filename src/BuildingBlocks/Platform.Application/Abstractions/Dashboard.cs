using Platform.Application.Security;

namespace Platform.Application.Abstractions;

public sealed record AudienceDashboardSection(long Subscribers, double? Delta, IReadOnlyList<long> Trend);

public sealed record CampaignDashboardSection(int SentLast30Days, double? AverageOpenRate);

public sealed record FormDashboardSection(int ResponsesLast30Days, int LiveForms);

public sealed class DashboardBuilder
{
    public AudienceDashboardSection? Audience { get; set; }
    public CampaignDashboardSection? Campaigns { get; set; }
    public FormDashboardSection? Forms { get; set; }
    public int ScheduledCampaigns { get; set; }
    public bool DomainVerified { get; set; }
    public bool HasAudience { get; set; }
    public bool HasForm { get; set; }
    public bool PostalAddressSet { get; set; }
}

public interface IDashboardContributor
{
    Task ContributeAsync(DashboardBuilder builder, AccessProfile access, CancellationToken cancellationToken);
}
