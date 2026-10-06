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
    Guid Id, string ReceiptNumber, Guid? PersonId, string? DonorName, string? DonorEmail, Guid? UnitId, Guid? BatchId,
    Guid? OccurrenceId, Guid? CampaignId, DateOnly ReceivedOn, string Method, string Channel, string Status, decimal Amount,
    string Currency, IReadOnlyList<AllocationResponse> Allocations, string? Reference, string? Notes, bool IsLocked,
    DateTimeOffset CreatedAt);

public sealed record RecordDonationRequest(
    Guid? PersonId, string? DonorName, string? DonorEmail, DateOnly ReceivedOn, PaymentMethod Method, IReadOnlyList<AllocationDto> Allocations,
    string? Currency = null, GivingChannel Channel = GivingChannel.InPerson, Guid? UnitId = null, Guid? BatchId = null,
    Guid? OccurrenceId = null, Guid? CampaignId = null, string? Reference = null, string? Notes = null);

public sealed record StatusChangeRequest(string Reason);

public sealed record DonationQuery(
    int Page = 1, int PageSize = 50, DateOnly? From = null, DateOnly? To = null, Guid? PersonId = null, Guid? FundId = null,
    Guid? BatchId = null, Guid? CampaignId = null, Guid? UnitId = null, DonationStatus? Status = null, PaymentMethod? Method = null, string? Search = null);

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
    public Task<Result<Donation>> RecordAsync(RecordDonationRequest r, DonationStatus status, CancellationToken ct) =>
        RecordAsync(r, status, null, ct);

    public async Task<Result<Donation>> RecordAsync(RecordDonationRequest r, DonationStatus status, Guid? effectiveUnitId, CancellationToken ct)
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
            tenantId, $"R{r.ReceivedOn.Year}-{number:D6}", ToDonor(r), ToGift(r, currency, effectiveUnitId), ToAllocations(r), status, clock.GetUtcNow());
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

    public static GiftDetails ToGift(RecordDonationRequest r, string currency, Guid? effectiveUnitId = null) =>
        new(r.ReceivedOn, r.Method, r.Channel, currency, effectiveUnitId ?? r.UnitId, r.OccurrenceId, r.CampaignId, r.Reference, r.Notes);

    public static IReadOnlyCollection<(Guid FundId, decimal Amount)> ToAllocations(RecordDonationRequest r) =>
        r.Allocations.Select(a => (a.FundId, a.Amount)).ToList();
}

public static class DonationEndpoints
{
    internal static readonly Error NotFound = Error.NotFound("donation.not_found", "The donation was not found.");

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        foreach (var prefix in new[] { "giving/donations", "donations" })
        {
            var group = endpoints.MapModuleGroup(prefix, "Giving");
            group.MapGet("/", List).RequirePermission(Permissions.Giving.View).WithSummary("Search donations");
            group.MapGet("/keyset", KeysetList).RequirePermission(Permissions.Giving.View).WithSummary("Keyset paginated donations");
            group.MapGet("/{id:guid}", Get).RequirePermission(Permissions.Giving.View).WithSummary("Get a donation");
            group.MapPost("/", Record).WithValidation<RecordDonationRequest>().RequirePermission(Permissions.Giving.Manage).WithSummary("Record a gift (cash, cheque, transfer…)");
            group.MapPut("/{id:guid}", Update).WithValidation<RecordDonationRequest>().RequirePermission(Permissions.Giving.Manage).WithSummary("Correct a gift (open batches only)");
            group.MapPost("/{id:guid}/void", Void).RequirePermission(Permissions.Giving.Manage).WithSummary("Void a gift recorded in error");
            group.MapPost("/{id:guid}/refund", Refund).RequirePermission(Permissions.Giving.Manage).WithSummary("Mark a completed gift as refunded");
        }
    }

    internal static async Task<List<DonationResponse>> ToResponsesAsync(GivingDbContext db, IReadOnlyList<Donation> donations, CancellationToken ct)
    {
        var fundIds = donations.SelectMany(d => d.Allocations).Select(a => a.FundId).Distinct().ToList();
        var funds = await db.Funds.IgnoreQueryFilters([QueryFilters.SoftDelete]).AsNoTracking()
            .Where(f => fundIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);

        return donations.Select(d => new DonationResponse(
            d.Id, d.ReceiptNumber, d.PersonId, d.DonorName, d.DonorEmail, d.UnitId, d.BatchId, d.OccurrenceId, d.CampaignId,
            d.ReceivedOn, d.Method.ToString(), d.Channel.ToString(), d.Status.ToString(), d.Total.Amount, d.Total.Currency,
            d.Allocations.Select(a => new AllocationResponse(a.FundId, funds.GetValueOrDefault(a.FundId, "?"), a.Amount)).ToList(),
            d.Reference, d.Notes, d.IsLocked, d.CreatedAt)).ToList();
    }

    private static async Task<Donation?> FindScopedAsync(
        Guid id,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        bool tracking,
        CancellationToken ct)
    {
        var query = tracking ? db.Donations.Include(d => d.Allocations).AsQueryable() : db.Donations.AsNoTracking().Include(d => d.Allocations).AsQueryable();
        var donation = await query.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (donation is null)
        {
            return null;
        }

        var access = await currentAccess.GetAsync(ct);
        if (access?.ScopeUnitId is { } scopeUnit)
        {
            var allowed = await units.GetSubtreeIdsAsync(scopeUnit, ct);
            if (donation.UnitId is not { } unitId || !allowed.Contains(unitId))
            {
                return null;
            }
        }

        return donation;
    }

    private static async Task<IResult> List(
        [AsParameters] DonationQuery q,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
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

        var access = await currentAccess.GetAsync(ct);
        if (access?.ScopeUnitId is { } scopeUnit)
        {
            var allowed = await units.GetSubtreeIdsAsync(scopeUnit, ct);
            if (q.UnitId is { } req)
            {
                if (!allowed.Contains(req))
                {
                    return Results.Ok(new PagedResult<DonationResponse>([], page.SafePage, page.SafePageSize, 0));
                }
                query = query.Where(d => d.UnitId == req);
            }
            else
            {
                query = query.Where(d => d.UnitId != null && allowed.Contains(d.UnitId.Value));
            }
        }
        else if (q.UnitId is { } uid)
        {
            query = query.Where(d => d.UnitId == uid);
        }

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

    private static async Task<IResult> KeysetList(
        [AsParameters] KeysetRequest<string> request,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var limit = request.SafeLimit;
        var query = db.Donations.AsNoTracking().Include(d => d.Allocations).AsQueryable();

        var access = await currentAccess.GetAsync(ct);
        if (access?.ScopeUnitId is { } scopeUnit)
        {
            var allowed = await units.GetSubtreeIdsAsync(scopeUnit, ct);
            query = query.Where(d => d.UnitId != null && allowed.Contains(d.UnitId.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            var parts = request.Cursor.Split('_', 2);
            if (parts.Length == 2 && DateOnly.TryParse(parts[0], out var cursorDate) && Guid.TryParse(parts[1], out var cursorId))
            {
                if (request.Ascending)
                {
                    query = query.Where(d => d.ReceivedOn > cursorDate || (d.ReceivedOn == cursorDate && d.Id.CompareTo(cursorId) > 0));
                }
                else
                {
                    query = query.Where(d => d.ReceivedOn < cursorDate || (d.ReceivedOn == cursorDate && d.Id.CompareTo(cursorId) < 0));
                }
            }
        }

        query = request.Ascending
            ? query.OrderBy(d => d.ReceivedOn).ThenBy(d => d.Id)
            : query.OrderByDescending(d => d.ReceivedOn).ThenByDescending(d => d.Id);

        var fetched = await query.Take(limit + 1).ToListAsync(ct);
        var hasMore = fetched.Count > limit;
        var items = hasMore ? fetched.Take(limit).ToList() : fetched;

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = $"{last.ReceivedOn:yyyy-MM-dd}_{last.Id}";
        }

        var responses = await ToResponsesAsync(db, items, ct);
        return Results.Ok(new KeysetResponse<DonationResponse, string>(responses, nextCursor, hasMore));
    }

    private static async Task<IResult> Get(
        Guid id,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var donation = await FindScopedAsync(id, db, currentAccess, units, tracking: false, ct);
        return donation is null ? NotFound.ToError() : Results.Ok((await ToResponsesAsync(db, [donation], ct))[0]);
    }

    private static async Task<IResult> Record(
        RecordDonationRequest r,
        DonationRecorder recorder,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var access = await currentAccess.GetAsync(ct);
        Guid? effectiveUnitId;
        if (access?.ScopeUnitId is { } scopeUnit)
        {
            var allowed = await units.GetSubtreeIdsAsync(scopeUnit, ct);
            if (r.UnitId is { } requested && !allowed.Contains(requested))
            {
                return Error.Validation("donation.invalid_unit", "You do not have access to record gifts for this unit.").ToError();
            }
            effectiveUnitId = r.UnitId ?? scopeUnit;
        }
        else
        {
            effectiveUnitId = r.UnitId;
        }

        var result = await recorder.RecordAsync(r, DonationStatus.Completed, effectiveUnitId, ct);
        return result.IsFailure
            ? result.Error.ToError()
            : Results.Created($"/api/v1/donations/{result.Value.Id}", (await ToResponsesAsync(db, [result.Value], ct))[0]);
    }

    private static async Task<IResult> Update(
        Guid id,
        RecordDonationRequest r,
        GivingDbContext db,
        DonationRecorder recorder,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var donation = await FindScopedAsync(id, db, currentAccess, units, tracking: true, ct);
        if (donation is null)
        {
            return NotFound.ToError();
        }

        var currency = r.Currency?.ToUpperInvariant() ?? donation.Total.Currency;
        var validation = await recorder.ValidateReferencesAsync(r, currency, ct);
        if (validation.IsFailure)
        {
            return validation.Error.ToError();
        }

        donation.Update(DonationRecorder.ToDonor(r), DonationRecorder.ToGift(r, currency), DonationRecorder.ToAllocations(r));
        donation.AssignToBatch(r.BatchId);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToResponsesAsync(db, [donation], ct))[0]);
    }

    private static async Task<IResult> Void(
        Guid id,
        StatusChangeRequest r,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        CancellationToken ct)
    {
        var donation = await FindScopedAsync(id, db, currentAccess, units, tracking: true, ct);
        if (donation is null)
        {
            return NotFound.ToError();
        }

        donation.Void(r.Reason);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Refund(
        Guid id,
        StatusChangeRequest r,
        GivingDbContext db,
        ICurrentAccess currentAccess,
        IUnitDirectory units,
        TimeProvider clock,
        CancellationToken ct)
    {
        var donation = await FindScopedAsync(id, db, currentAccess, units, tracking: true, ct);
        if (donation is null)
        {
            return NotFound.ToError();
        }

        donation.Refund(clock.GetUtcNow(), r.Reason);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
