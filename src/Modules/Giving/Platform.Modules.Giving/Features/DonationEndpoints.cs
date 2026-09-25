using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Pagination;
using Platform.Application.Security;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Giving.Domain;
using Platform.Modules.Giving.Infrastructure;
using Platform.Modules.People.Contracts;
using Platform.Modules.Tenancy.Contracts;
using Platform.SharedKernel.Domain;
using Platform.SharedKernel.Results;
using Platform.Web.Endpoints;
using Platform.Web.Security;

namespace Platform.Modules.Giving.Features;

public sealed record AllocationDto(Guid FundId, decimal Amount);

public sealed record AllocationResponse(Guid FundId, string FundName, decimal Amount);

public sealed record DonationResponse(
    Guid Id, string ReceiptNumber, Guid? PersonId, string? DonorName, string? DonorEmail, Guid? BranchId, Guid? BatchId,
    Guid? OccurrenceId, Guid? CampaignId, DateOnly ReceivedOn, string Method, string Channel, string Status, decimal Amount,
    string Currency, IReadOnlyList<AllocationResponse> Allocations, string? Reference, string? Notes, bool IsLocked,
    DateTimeOffset CreatedAt);

public sealed record RecordDonationRequest(
    Guid? PersonId, string? DonorName, string? DonorEmail, DateOnly ReceivedOn, PaymentMethod Method, IReadOnlyList<AllocationDto> Allocations,
    string? Currency = null, GivingChannel Channel = GivingChannel.InPerson, Guid? BranchId = null, Guid? BatchId = null,
    Guid? OccurrenceId = null, Guid? CampaignId = null, string? Reference = null, string? Notes = null);

public sealed record StatusChangeRequest(string Reason);

public sealed record DonationQuery(
    int Page = 1, int PageSize = 50, DateOnly? From = null, DateOnly? To = null, Guid? PersonId = null, Guid? FundId = null,
    Guid? BatchId = null, Guid? CampaignId = null, DonationStatus? Status = null, PaymentMethod? Method = null, string? Search = null);

internal sealed class RecordDonationValidator : AbstractValidator<RecordDonationRequest>
{
    public RecordDonationValidator()
    {
        RuleFor(x => x.Allocations).NotEmpty().WithMessage("Allocate the gift to at least one fund.");
        RuleForEach(x => x.Allocations).ChildRules(a =>
        {
            a.RuleFor(x => x.FundId).NotEmpty();
            a.RuleFor(x => x.Amount).GreaterThan(0).LessThan(1_000_000_000m).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        });
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Currency).Length(3).When(x => x.Currency is not null);
        RuleFor(x => x.DonorName).MaximumLength(200);
        RuleFor(x => x.DonorEmail).EmailAddress().MaximumLength(256);
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

/// <summary>Records gifts with receipt numbers, validating funds, batches and currency.</summary>
internal sealed class DonationRecorder(GivingDbContext db, ITenantContext tenant, ITenantDirectory tenants, TimeProvider clock)
{
    public async Task<Result<Donation>> RecordAsync(RecordDonationRequest r, DonationStatus status, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var currency = r.Currency?.ToUpperInvariant() ?? (await tenants.GetAsync(tenantId, ct))?.DefaultCurrency ?? "USD";

        var validation = await ValidateReferencesAsync(r, currency, ct);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        var number = await db.NextNumberAsync(tenantId, $"receipt-{r.ReceivedOn.Year}", ct);
        var donation = Donation.Record(
            tenantId, $"R{r.ReceivedOn.Year}-{number:D6}", ToDonor(r), ToGift(r, currency), ToAllocations(r), status, clock.GetUtcNow());
        donation.AssignToBatch(r.BatchId);
        db.Donations.Add(donation);
        await db.SaveChangesAsync(ct);
        return donation;
    }

    public async Task<Result> ValidateReferencesAsync(RecordDonationRequest r, string currency, CancellationToken ct)
    {
        var fundIds = r.Allocations.Select(a => a.FundId).Distinct().ToList();
        var activeFunds = await db.Funds.CountAsync(f => fundIds.Contains(f.Id) && f.IsActive, ct);
        if (activeFunds != fundIds.Count)
        {
            return Error.Validation("donation.invalid_fund", "One or more funds do not exist or are inactive.");
        }

        if (r.BatchId is { } batchId)
        {
            var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
            if (batch is null || batch.Status != BatchStatus.Open)
            {
                return Error.Conflict("donation.batch_closed", "The batch does not exist or is closed.");
            }

            if (!string.Equals(batch.Currency, currency, StringComparison.Ordinal))
            {
                return Error.Validation("donation.currency_mismatch", $"The batch is in {batch.Currency}.");
            }
        }

        if (r.CampaignId is { } campaignId && !await db.Campaigns.AnyAsync(c => c.Id == campaignId, ct))
        {
            return Error.Validation("donation.invalid_campaign", "The campaign does not exist.");
        }

        return Result.Success();
    }

    public static DonorInfo ToDonor(RecordDonationRequest r) => new(r.PersonId, r.DonorName, r.DonorEmail);

    public static GiftDetails ToGift(RecordDonationRequest r, string currency) =>
        new(r.ReceivedOn, r.Method, r.Channel, currency, r.BranchId, r.OccurrenceId, r.CampaignId, r.Reference, r.Notes);

    public static IReadOnlyCollection<(Guid FundId, decimal Amount)> ToAllocations(RecordDonationRequest r) =>
        r.Allocations.Select(a => (a.FundId, a.Amount)).ToList();
}

public static class DonationEndpoints
{
    internal static readonly Error NotFound = Error.NotFound("donation.not_found", "The donation was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapModuleGroup("donations", "Giving");
        group.MapGet("/", List).RequirePermission(Permissions.Giving.Read).WithSummary("Search donations");
        group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.Giving.Read).WithSummary("Get a donation");
        group.MapPost("/", Record).WithValidation<RecordDonationRequest>().RequirePermission(Permissions.Giving.Record).WithSummary("Record a gift (cash, cheque, transfer…)");
        group.MapPut("/{id:guid}", Update).WithValidation<RecordDonationRequest>().RequirePermission(Permissions.Giving.Record).WithSummary("Correct a gift (open batches only)");
        group.MapPost("/{id:guid}/void", Void).RequirePermission(Permissions.Giving.Record).WithSummary("Void a gift recorded in error");
        group.MapPost("/{id:guid}/refund", Refund).RequirePermission(Permissions.Giving.Refund).WithSummary("Mark a completed gift as refunded");
    }

    internal static async Task<List<DonationResponse>> ToResponsesAsync(GivingDbContext db, IReadOnlyList<Donation> donations, CancellationToken ct)
    {
        var fundIds = donations.SelectMany(d => d.Allocations).Select(a => a.FundId).Distinct().ToList();
        var funds = await db.Funds.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(f => fundIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        return donations.Select(d => new DonationResponse(
            d.Id, d.ReceiptNumber, d.PersonId, d.DonorName, d.DonorEmail, d.BranchId, d.BatchId, d.OccurrenceId, d.CampaignId,
            d.ReceivedOn, d.Method.ToString(), d.Channel.ToString(), d.Status.ToString(), d.Total.Amount, d.Total.Currency,
            d.Allocations.Select(a => new AllocationResponse(a.FundId, funds.GetValueOrDefault(a.FundId, "?"), a.Amount)).ToList(),
            d.Reference, d.Notes, d.IsLocked, d.CreatedAt)).ToList();
    }

    private static async Task<IResult> List([AsParameters] DonationQuery q, GivingDbContext db, CancellationToken ct)
    {
        var page = new PageRequest(q.Page, q.PageSize);
        var query = db.Donations.AsNoTracking().Include(d => d.Allocations).AsQueryable();
        if (q.From is { } from) query = query.Where(d => d.ReceivedOn >= from);
        if (q.To is { } to) query = query.Where(d => d.ReceivedOn <= to);
        if (q.PersonId is { } personId) query = query.Where(d => d.PersonId == personId);
        if (q.FundId is { } fundId) query = query.Where(d => d.Allocations.Any(a => a.FundId == fundId));
        if (q.BatchId is { } batchId) query = query.Where(d => d.BatchId == batchId);
        if (q.CampaignId is { } campaignId) query = query.Where(d => d.CampaignId == campaignId);
        if (q.Status is { } status) query = query.Where(d => d.Status == status);
        if (q.Method is { } method) query = query.Where(d => d.Method == method);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            query = query.Where(d => d.ReceiptNumber == term.ToUpperInvariant() || EF.Functions.ILike(d.DonorName ?? "", $"%{term}%") ||
                                     EF.Functions.ILike(d.Reference ?? "", $"%{term}%"));
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(d => d.ReceivedOn).ThenByDescending(d => d.CreatedAt)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<DonationResponse>(await ToResponsesAsync(db, items, ct), page.SafePage, page.SafePageSize, total));
    }

    private static async Task<IResult> Get(Guid id, GivingDbContext db, CancellationToken ct)
    {
        var donation = await db.Donations.AsNoTracking().Include(d => d.Allocations).FirstOrDefaultAsync(d => d.Id == id, ct);
        return donation is null ? NotFound.ToProblem() : Results.Ok((await ToResponsesAsync(db, [donation], ct))[0]);
    }

    private static async Task<IResult> Record(RecordDonationRequest r, DonationRecorder recorder, GivingDbContext db, CancellationToken ct)
    {
        var result = await recorder.RecordAsync(r, DonationStatus.Completed, ct);
        return result.IsFailure
            ? result.Error.ToProblem()
            : Results.Created($"/api/v1/donations/{result.Value.Id}", (await ToResponsesAsync(db, [result.Value], ct))[0]);
    }

    private static async Task<IResult> Update(Guid id, RecordDonationRequest r, GivingDbContext db, DonationRecorder recorder, CancellationToken ct)
    {
        var donation = await db.Donations.Include(d => d.Allocations).FirstOrDefaultAsync(d => d.Id == id, ct);
        if (donation is null)
        {
            return NotFound.ToProblem();
        }

        var currency = r.Currency?.ToUpperInvariant() ?? donation.Total.Currency;
        var validation = await recorder.ValidateReferencesAsync(r, currency, ct);
        if (validation.IsFailure)
        {
            return validation.Error.ToProblem();
        }

        donation.Update(DonationRecorder.ToDonor(r), DonationRecorder.ToGift(r, currency), DonationRecorder.ToAllocations(r));
        donation.AssignToBatch(r.BatchId);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToResponsesAsync(db, [donation], ct))[0]);
    }

    private static async Task<IResult> Void(Guid id, StatusChangeRequest r, GivingDbContext db, CancellationToken ct)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (donation is null)
        {
            return NotFound.ToProblem();
        }

        donation.Void(r.Reason);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Refund(Guid id, StatusChangeRequest r, GivingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (donation is null)
        {
            return NotFound.ToProblem();
        }

        donation.Refund(clock.GetUtcNow(), r.Reason);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
