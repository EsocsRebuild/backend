using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Modules.Giving.Domain;
using Platform.Modules.Giving.Infrastructure;
using Platform.Modules.People.Contracts;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Giving.Features;

public sealed record FundResponse(Guid Id, string Name, string Code, string? Description, bool IsActive, bool IsTaxDeductible, bool IsPublic, int SortOrder);

public sealed record SaveFundRequest(string Name, string Code, string? Description, bool IsActive = true, bool IsTaxDeductible = true, bool IsPublic = true, int SortOrder = 0);

public sealed record CampaignResponse(Guid Id, string Name, string? Description, Guid FundId, decimal Goal, string Currency, DateOnly StartsOn,
    DateOnly? EndsOn, bool IsActive, decimal Raised, decimal Pledged, int GiverCount);

public sealed record SaveCampaignRequest(string Name, string? Description, Guid FundId, decimal Goal, string? Currency, DateOnly StartsOn, DateOnly? EndsOn, bool IsActive = true);

public sealed record PledgeResponse(Guid Id, Guid CampaignId, Guid PersonId, string? PersonName, decimal Amount, string Currency, string Frequency,
    DateOnly PledgedOn, string Status, decimal Given, string? Notes);

public sealed record SavePledgeRequest(Guid CampaignId, Guid PersonId, decimal Amount, PledgeFrequency Frequency, DateOnly? PledgedOn,
    PledgeStatus Status = PledgeStatus.Active, string? Notes = null);

public sealed record BatchResponse(Guid Id, string Name, DateOnly BatchDate, Guid? BranchId, Guid? OccurrenceId, string Currency,
    decimal? ExpectedTotal, decimal RecordedTotal, int DonationCount, decimal? Variance, string Status, DateTimeOffset? ClosedAt);

public sealed record SaveBatchRequest(string Name, DateOnly BatchDate, string? Currency, Guid? BranchId, Guid? OccurrenceId, decimal? ExpectedTotal);

public sealed record GivingSummaryResponse(
    DateOnly From, DateOnly To, string Currency, decimal Total, int GiftCount, int GiverCount, decimal AverageGift,
    IReadOnlyList<NamedTotal> ByFund, IReadOnlyList<NamedTotal> ByMethod, IReadOnlyList<NamedTotal> ByMonth);

public sealed record NamedTotal(string Name, decimal Total, int Count);

public sealed record StatementResponse(Guid PersonId, string? PersonName, int Year, string Currency, decimal Total,
    IReadOnlyList<NamedTotal> ByFund, IReadOnlyList<StatementLine> Gifts);

public sealed record StatementLine(string ReceiptNumber, DateOnly ReceivedOn, string Method, decimal Amount, IReadOnlyList<AllocationResponse> Allocations);

internal sealed class SaveFundValidator : AbstractValidator<SaveFundRequest>
{
    public SaveFundValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(16).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

internal sealed class SaveCampaignValidator : AbstractValidator<SaveCampaignRequest>
{
    public SaveCampaignValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Goal).GreaterThan(0);
        RuleFor(x => x.Currency).Length(3).When(x => x.Currency is not null);
        RuleFor(x => x.EndsOn).GreaterThanOrEqualTo(x => x.StartsOn).When(x => x.EndsOn.HasValue);
    }
}

internal sealed class SavePledgeValidator : AbstractValidator<SavePledgeRequest>
{
    public SavePledgeValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Frequency).IsInEnum();
        RuleFor(x => x.Status).IsInEnum();
    }
}

internal sealed class SaveBatchValidator : AbstractValidator<SaveBatchRequest>
{
    public SaveBatchValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Currency).Length(3).When(x => x.Currency is not null);
        RuleFor(x => x.ExpectedTotal).GreaterThanOrEqualTo(0).When(x => x.ExpectedTotal.HasValue);
    }
}

public static class GivingManagementEndpoints
{
    private static readonly Error FundNotFound = Error.NotFound("fund.not_found", "The fund was not found.");
    private static readonly Error CampaignNotFound = Error.NotFound("campaign.not_found", "The campaign was not found.");
    private static readonly Error BatchNotFound = Error.NotFound("batch.not_found", "The batch was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var funds = endpoints.MapModuleGroup("funds", "Giving");
        funds.MapGet("/", ListFunds).RequirePermission(Permissions.Giving.Read).WithSummary("List funds");
        funds.MapPost("/", CreateFund).WithValidation<SaveFundRequest>().RequirePermission(Permissions.Giving.FundsManage).WithSummary("Create a fund");
        funds.MapPut("/{id:guid}", UpdateFund).WithValidation<SaveFundRequest>().RequirePermission(Permissions.Giving.FundsManage).WithSummary("Update a fund");

        var campaigns = endpoints.MapModuleGroup("campaigns", "Giving");
        campaigns.MapGet("/", ListCampaigns).RequirePermission(Permissions.Giving.Read).WithSummary("Campaigns with progress");
        campaigns.MapPost("/", CreateCampaign).WithValidation<SaveCampaignRequest>().RequirePermission(Permissions.Giving.PledgesManage).WithSummary("Create a campaign");
        campaigns.MapPut("/{id:guid}", UpdateCampaign).WithValidation<SaveCampaignRequest>().RequirePermission(Permissions.Giving.PledgesManage).WithSummary("Update a campaign");
        campaigns.MapGet("/{id:guid}/pledges", ListPledges).RequirePermission(Permissions.Giving.Read).WithSummary("Pledges with fulfilment");

        var pledges = endpoints.MapModuleGroup("pledges", "Giving");
        pledges.MapPost("/", CreatePledge).WithValidation<SavePledgeRequest>().RequirePermission(Permissions.Giving.PledgesManage).WithSummary("Record a pledge");
        pledges.MapPut("/{id:guid}", UpdatePledge).WithValidation<SavePledgeRequest>().RequirePermission(Permissions.Giving.PledgesManage).WithSummary("Update a pledge");

        var batches = endpoints.MapModuleGroup("batches", "Giving");
        batches.MapGet("/", ListBatches).RequirePermission(Permissions.Giving.Read).WithSummary("Counting batches with reconciliation");
        batches.MapPost("/", OpenBatch).WithValidation<SaveBatchRequest>().RequirePermission(Permissions.Giving.BatchesManage).WithSummary("Open a batch");
        batches.MapPost("/{id:guid}/close", CloseBatch).RequirePermission(Permissions.Giving.BatchesManage).WithSummary("Close and lock a batch");
        batches.MapPost("/{id:guid}/reopen", ReopenBatch).RequirePermission(Permissions.Giving.BatchesManage).WithSummary("Reopen a closed batch");

        var reports = endpoints.MapModuleGroup("giving/reports", "Giving");
        reports.MapGet("/summary", Summary).RequirePermission(Permissions.Giving.Reports).WithSummary("Totals by fund, method and month");
        reports.MapGet("/statements/{personId:guid}", Statement).RequirePermission(Permissions.Giving.Read).WithSummary("Annual giving statement for a donor");

        endpoints.MapPublicGroup("giving", "Public")
            .MapGet("/funds", PublicFunds).WithSummary("Funds offered for online giving");
    }

    // ---- Funds -----------------------------------------------------------------------------

    private static FundResponse ToResponse(Fund f) => new(f.Id, f.Name, f.Code, f.Description, f.IsActive, f.IsTaxDeductible, f.IsPublic, f.SortOrder);

    private static async Task<IResult> ListFunds(GivingDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Funds.AsNoTracking().OrderBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync(ct)).Select(ToResponse));

    private static async Task<IResult> PublicFunds(HttpContext http, GivingDbContext db, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Ok(await db.Funds.AsNoTracking().Where(f => f.IsActive && f.IsPublic).OrderBy(f => f.SortOrder)
            .Select(f => new { f.Id, f.Name, f.Code, f.Description }).ToListAsync(ct));
    }

    private static async Task<IResult> CreateFund(SaveFundRequest r, GivingDbContext db, CancellationToken ct)
    {
        if (await db.Funds.AnyAsync(f => f.Code == r.Code.ToUpper(), ct))
        {
            return Error.Conflict("fund.code_taken", "Another fund uses this code.").ToProblem();
        }

        var fund = Fund.Create(r.Name, r.Code, r.Description, r.IsPublic, r.SortOrder);
        fund.Update(r.Name, r.Code, r.Description, r.IsActive, r.IsTaxDeductible, r.IsPublic, r.SortOrder);
        db.Funds.Add(fund);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/funds/{fund.Id}", ToResponse(fund));
    }

    private static async Task<IResult> UpdateFund(Guid id, SaveFundRequest r, GivingDbContext db, CancellationToken ct)
    {
        var fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fund is null)
        {
            return FundNotFound.ToProblem();
        }

        if (await db.Funds.AnyAsync(f => f.Code == r.Code.ToUpper() && f.Id != id, ct))
        {
            return Error.Conflict("fund.code_taken", "Another fund uses this code.").ToProblem();
        }

        fund.Update(r.Name, r.Code, r.Description, r.IsActive, r.IsTaxDeductible, r.IsPublic, r.SortOrder);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToResponse(fund));
    }

    // ---- Campaigns & pledges ---------------------------------------------------------------

    private static async Task<IResult> ListCampaigns(bool? activeOnly, GivingDbContext db, CancellationToken ct)
    {
        var query = db.Campaigns.AsNoTracking();
        if (activeOnly == true) query = query.Where(c => c.IsActive);

        var rows = await query.OrderByDescending(c => c.StartsOn)
            .Select(c => new
            {
                c,
                Raised = db.Donations.Where(d => d.CampaignId == c.Id && d.Status == DonationStatus.Completed).Sum(d => (decimal?)d.Total.Amount) ?? 0,
                Givers = db.Donations.Where(d => d.CampaignId == c.Id && d.Status == DonationStatus.Completed && d.PersonId != null).Select(d => d.PersonId).Distinct().Count(),
                Pledged = db.Pledges.Where(p => p.CampaignId == c.Id && p.Status != PledgeStatus.Cancelled).Sum(p => (decimal?)p.Amount.Amount) ?? 0,
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(x => new CampaignResponse(x.c.Id, x.c.Name, x.c.Description, x.c.FundId, x.c.Goal.Amount, x.c.Goal.Currency,
            x.c.StartsOn, x.c.EndsOn, x.c.IsActive, x.Raised, x.Pledged, x.Givers)));
    }

    private static async Task<IResult> CreateCampaign(SaveCampaignRequest r, GivingDbContext db, ITenantContext tenant, ITenantDirectory tenants, CancellationToken ct)
    {
        if (!await db.Funds.AnyAsync(f => f.Id == r.FundId, ct))
        {
            return FundNotFound.ToProblem();
        }

        var currency = r.Currency ?? (await tenants.GetAsync(tenant.RequiredTenantId, ct))!.DefaultCurrency;
        var campaign = Campaign.Create(r.Name, r.FundId, Money.Of(r.Goal, currency), r.StartsOn);
        campaign.Update(r.Name, r.Description, r.FundId, Money.Of(r.Goal, currency), r.StartsOn, r.EndsOn, r.IsActive);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/campaigns/{campaign.Id}", new { campaign.Id });
    }

    private static async Task<IResult> UpdateCampaign(Guid id, SaveCampaignRequest r, GivingDbContext db, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null)
        {
            return CampaignNotFound.ToProblem();
        }

        campaign.Update(r.Name, r.Description, r.FundId, Money.Of(r.Goal, r.Currency ?? campaign.Goal.Currency), r.StartsOn, r.EndsOn, r.IsActive);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPledges(Guid id, GivingDbContext db, IPeopleDirectory people, CancellationToken ct)
    {
        var pledges = await db.Pledges.AsNoTracking().Where(p => p.CampaignId == id).ToListAsync(ct);
        var personIds = pledges.Select(p => p.PersonId).ToList();
        var given = await db.Donations.AsNoTracking()
            .Where(d => d.CampaignId == id && d.Status == DonationStatus.Completed && d.PersonId != null && personIds.Contains(d.PersonId.Value))
            .GroupBy(d => d.PersonId!.Value).Select(g => new { g.Key, Total = g.Sum(d => d.Total.Amount) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var names = await people.GetSummariesAsync(personIds, ct);

        return Results.Ok(pledges.Select(p => new PledgeResponse(p.Id, p.CampaignId, p.PersonId, names.GetValueOrDefault(p.PersonId)?.FullName,
            p.Amount.Amount, p.Amount.Currency, p.Frequency.ToString(), p.PledgedOn, p.Status.ToString(), given.GetValueOrDefault(p.PersonId), p.Notes))
            .OrderBy(p => p.PersonName));
    }

    private static async Task<IResult> CreatePledge(SavePledgeRequest r, GivingDbContext db, IPeopleDirectory people, TimeProvider clock, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CampaignId, ct);
        if (campaign is null)
        {
            return CampaignNotFound.ToProblem();
        }

        if ((await people.GetSummariesAsync([r.PersonId], ct)).Count == 0)
        {
            return Error.NotFound("person.not_found", "The person was not found.").ToProblem();
        }

        var pledge = Pledge.Create(r.CampaignId, r.PersonId, Money.Of(r.Amount, campaign.Goal.Currency), r.Frequency,
            r.PledgedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), r.Notes);
        db.Pledges.Add(pledge);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/pledges/{pledge.Id}", new { pledge.Id });
    }

    private static async Task<IResult> UpdatePledge(Guid id, SavePledgeRequest r, GivingDbContext db, CancellationToken ct)
    {
        var pledge = await db.Pledges.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pledge is null)
        {
            return Error.NotFound("pledge.not_found", "The pledge was not found.").ToProblem();
        }

        pledge.Update(Money.Of(r.Amount, pledge.Amount.Currency), r.Frequency, r.Status, r.Notes);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Batches ---------------------------------------------------------------------------

    private static async Task<IResult> ListBatches(BatchStatus? status, GivingDbContext db, CancellationToken ct)
    {
        var query = db.Batches.AsNoTracking();
        if (status is { } s) query = query.Where(b => b.Status == s);

        var rows = await query.OrderByDescending(b => b.BatchDate).Take(200)
            .Select(b => new
            {
                b,
                Total = db.Donations.Where(d => d.BatchId == b.Id && d.Status == DonationStatus.Completed).Sum(d => (decimal?)d.Total.Amount) ?? 0,
                Count = db.Donations.Count(d => d.BatchId == b.Id && d.Status == DonationStatus.Completed),
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(x => new BatchResponse(x.b.Id, x.b.Name, x.b.BatchDate, x.b.BranchId, x.b.OccurrenceId, x.b.Currency,
            x.b.ExpectedTotal, x.Total, x.Count, x.b.ExpectedTotal is { } e ? x.Total - e : null, x.b.Status.ToString(), x.b.ClosedAt)));
    }

    private static async Task<IResult> OpenBatch(SaveBatchRequest r, GivingDbContext db, ITenantContext tenant, ITenantDirectory tenants, CancellationToken ct)
    {
        var currency = r.Currency ?? (await tenants.GetAsync(tenant.RequiredTenantId, ct))!.DefaultCurrency;
        var batch = DonationBatch.Open(r.Name, r.BatchDate, currency, r.BranchId, r.OccurrenceId, r.ExpectedTotal);
        db.Batches.Add(batch);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/batches/{batch.Id}", new { batch.Id });
    }

    private static async Task<IResult> CloseBatch(Guid id, GivingDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
        {
            return BatchNotFound.ToProblem();
        }

        var donations = await db.Donations.Where(d => d.BatchId == id).ToListAsync(ct);
        batch.Close(donations, clock.GetUtcNow(), user.UserId);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReopenBatch(Guid id, GivingDbContext db, CancellationToken ct)
    {
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
        {
            return BatchNotFound.ToProblem();
        }

        batch.Reopen(await db.Donations.Where(d => d.BatchId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---- Reports ---------------------------------------------------------------------------

    private static async Task<IResult> Summary(DateOnly from, DateOnly to, string? currency, Guid? branchId, GivingDbContext db,
        ITenantContext tenant, ITenantDirectory tenants, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 366 * 3)
        {
            return Error.Validation("report.range", "Use a range of at most three years.").ToProblem();
        }

        var cur = currency?.ToUpperInvariant() ?? (await tenants.GetAsync(tenant.RequiredTenantId, ct))!.DefaultCurrency;
        var donations = db.Donations.AsNoTracking()
            .Where(d => d.Status == DonationStatus.Completed && d.ReceivedOn >= from && d.ReceivedOn <= to && d.Total.Currency == cur);
        if (branchId is { } b) donations = donations.Where(d => d.BranchId == b);

        var total = await donations.SumAsync(d => (decimal?)d.Total.Amount, ct) ?? 0;
        var count = await donations.CountAsync(ct);
        var givers = await donations.Where(d => d.PersonId != null).Select(d => d.PersonId).Distinct().CountAsync(ct);

        var donationIds = donations.Select(d => d.Id);
        var fundTotals = await db.Allocations.AsNoTracking()
            .Where(a => donationIds.Contains(a.DonationId))
            .GroupBy(a => a.FundId)
            .Select(g => new { FundId = g.Key, Total = g.Sum(a => a.Amount), Count = g.Count() })
            .ToListAsync(ct);
        var fundIds = fundTotals.Select(f => f.FundId).ToList();
        var fundNames = await db.Funds.IgnoreQueryFilters([Platform.Infrastructure.Persistence.QueryFilters.SoftDelete]).AsNoTracking()
            .Where(f => fundIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var byFund = fundTotals.Select(f => new NamedTotal(fundNames.GetValueOrDefault(f.FundId, "?"), f.Total, f.Count))
            .OrderByDescending(x => x.Total).ToList();

        var byMethod = (await donations.GroupBy(d => d.Method).Select(g => new { g.Key, Total = g.Sum(d => d.Total.Amount), Count = g.Count() }).ToListAsync(ct))
            .Select(x => new NamedTotal(x.Key.ToString(), x.Total, x.Count)).OrderByDescending(x => x.Total).ToList();

        var byMonth = (await donations.GroupBy(d => new { d.ReceivedOn.Year, d.ReceivedOn.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(d => d.Total.Amount), Count = g.Count() }).ToListAsync(ct))
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .Select(x => new NamedTotal($"{x.Year}-{x.Month:D2}", x.Total, x.Count)).ToList();

        return Results.Ok(new GivingSummaryResponse(from, to, cur, total, count, givers,
            count == 0 ? 0 : decimal.Round(total / count, 2), byFund, byMethod, byMonth));
    }

    internal static async Task<StatementResponse> BuildStatementAsync(GivingDbContext db, Guid personId, string? personName, int year, CancellationToken ct)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var gifts = await db.Donations.AsNoTracking().Include(d => d.Allocations)
            .Where(d => d.PersonId == personId && d.Status == DonationStatus.Completed && d.ReceivedOn >= from && d.ReceivedOn <= to)
            .OrderBy(d => d.ReceivedOn).ToListAsync(ct);
        var responses = await DonationEndpoints.ToResponsesAsync(db, gifts, ct);

        var currency = gifts.FirstOrDefault()?.Total.Currency ?? string.Empty;
        return new StatementResponse(personId, personName, year, currency, gifts.Sum(g => g.Total.Amount),
            responses.SelectMany(r => r.Allocations).GroupBy(a => a.FundName)
                .Select(g => new NamedTotal(g.Key, g.Sum(a => a.Amount), g.Count())).OrderByDescending(x => x.Total).ToList(),
            responses.Select(r => new StatementLine(r.ReceiptNumber, r.ReceivedOn, r.Method, r.Amount, r.Allocations)).ToList());
    }

    private static async Task<IResult> Statement(Guid personId, int? year, GivingDbContext db, IPeopleDirectory people, TimeProvider clock, CancellationToken ct)
    {
        var person = (await people.GetSummariesAsync([personId], ct)).GetValueOrDefault(personId);
        return Results.Ok(await BuildStatementAsync(db, personId, person?.FullName, year ?? clock.GetUtcNow().Year, ct));
    }
}
