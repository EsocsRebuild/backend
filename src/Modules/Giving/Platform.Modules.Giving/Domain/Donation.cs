using Platform.Modules.Giving.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Giving.Domain;

public enum PaymentMethod
{
    Cash,
    Cheque,
    BankTransfer,
    Card,
    MobileMoney,
    Online,
    Other,
}

public enum GivingChannel
{
    InPerson,
    Website,
    MobileApp,
    Import,
}

public enum DonationStatus
{
    /// <summary>Online payment started, awaiting provider confirmation.</summary>
    Pending,
    Completed,
    Failed,
    Refunded,

    /// <summary>Recorded in error and cancelled (kept for audit; excluded from totals).</summary>
    Voided,
}

/// <summary>
/// A gift. The total can be split across several funds (e.g. tithe + building) via
/// <see cref="Allocations"/>; the allocations must always add up to <see cref="Total"/>.
/// Amounts are exact decimals with an ISO currency — never floating point.
/// </summary>
public sealed class Donation : TenantAggregateRoot
{
    private readonly List<DonationAllocation> _allocations = [];

    private Donation() { }

    /// <summary>Sequential, human-readable receipt number, e.g. "R2026-000123".</summary>
    public string ReceiptNumber { get; private set; } = null!;

    /// <summary>Known donor (People module id); null for anonymous or non-member gifts.</summary>
    public Guid? PersonId { get; private set; }

    public string? DonorName { get; private set; }
    public string? DonorEmail { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? BatchId { get; private set; }

    /// <summary>Service / event occurrence the offering was received at (Events module id).</summary>
    public Guid? OccurrenceId { get; private set; }

    public Guid? CampaignId { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public PaymentMethod Method { get; private set; }
    public GivingChannel Channel { get; private set; }
    public DonationStatus Status { get; private set; }
    public Money Total { get; private set; } = null!;

    /// <summary>Cheque number, bank transfer reference, etc.</summary>
    public string? Reference { get; private set; }

    public string? Provider { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public string? StatusReason { get; private set; }

    public IReadOnlyCollection<DonationAllocation> Allocations => _allocations.AsReadOnly();

    public static Donation Record(
        Guid tenantId, string receiptNumber, DonorInfo donor, GiftDetails gift, IReadOnlyCollection<(Guid FundId, decimal Amount)> allocations,
        DonationStatus status, DateTimeOffset now)
    {
        var donation = new Donation
        {
            ReceiptNumber = receiptNumber,
            Status = DonationStatus.Pending,
        };
        donation.AssignTenant(tenantId);
        donation.ApplyDonor(donor);
        donation.ApplyGift(gift);
        donation.SetAllocations(allocations, gift.Currency);

        if (status == DonationStatus.Completed)
        {
            donation.Complete(now, providerReference: null);
        }

        return donation;
    }

    public void Update(DonorInfo donor, GiftDetails gift, IReadOnlyCollection<(Guid FundId, decimal Amount)> allocations)
    {
        EnsureEditable();
        ApplyDonor(donor);
        ApplyGift(gift);
        SetAllocations(allocations, gift.Currency);
    }

    public void AttachProvider(string provider, string providerReference)
    {
        Provider = provider;
        ProviderReference = providerReference;
    }

    public void Complete(DateTimeOffset now, string? providerReference)
    {
        if (Status == DonationStatus.Completed)
        {
            return;
        }

        if (Status != DonationStatus.Pending)
        {
            throw new DomainException($"A {Status} donation cannot be completed.");
        }

        Status = DonationStatus.Completed;
        CompletedAt = now;
        ProviderReference ??= providerReference;
        Raise(new DonationCompletedIntegrationEvent(TenantId, Id, ReceiptNumber, PersonId, DonorEmail, DonorName,
            Total.Amount, Total.Currency, ReceivedOn, Channel.ToString()));
    }

    public void Fail(string reason)
    {
        if (Status == DonationStatus.Pending)
        {
            Status = DonationStatus.Failed;
            StatusReason = reason;
        }
    }

    public void Refund(DateTimeOffset now, string reason)
    {
        if (Status != DonationStatus.Completed)
        {
            throw new DomainException("Only completed donations can be refunded.");
        }

        Status = DonationStatus.Refunded;
        RefundedAt = now;
        StatusReason = reason;
        Raise(new DonationRefundedIntegrationEvent(TenantId, Id, Total.Amount, Total.Currency, reason));
    }

    public void Void(string reason)
    {
        EnsureEditable();
        Status = DonationStatus.Voided;
        StatusReason = reason;
    }

    public void AssignToBatch(Guid? batchId)
    {
        if (batchId != BatchId)
        {
            EnsureEditable();
            BatchId = batchId;
        }
    }

    /// <summary>Set by the batch when it is closed: its donations become read-only.</summary>
    public bool IsLocked { get; private set; }

    internal void Lock() => IsLocked = true;

    internal void Unlock() => IsLocked = false;

    private void EnsureEditable()
    {
        if (IsLocked)
        {
            throw new DomainException("This donation belongs to a closed batch. Reopen the batch to edit it.");
        }

        if (Status is DonationStatus.Refunded or DonationStatus.Voided)
        {
            throw new DomainException($"A {Status} donation cannot be changed.");
        }
    }

    private void ApplyDonor(DonorInfo donor)
    {
        PersonId = donor.PersonId;
        DonorName = donor.Name?.Trim();
        DonorEmail = donor.Email?.Trim().ToLowerInvariant();
    }

    private void ApplyGift(GiftDetails gift)
    {
        BranchId = gift.BranchId;
        OccurrenceId = gift.OccurrenceId;
        CampaignId = gift.CampaignId;
        ReceivedOn = gift.ReceivedOn;
        Method = gift.Method;
        Channel = gift.Channel;
        Reference = gift.Reference;
        Notes = gift.Notes;
    }

    private void SetAllocations(IReadOnlyCollection<(Guid FundId, decimal Amount)> allocations, string currency)
    {
        if (allocations.Count == 0)
        {
            throw new DomainException("A donation must be allocated to at least one fund.");
        }

        if (allocations.Any(a => a.Amount <= 0))
        {
            throw new DomainException("Allocation amounts must be positive.");
        }

        if (allocations.GroupBy(a => a.FundId).Any(g => g.Count() > 1))
        {
            throw new DomainException("Each fund may appear only once per donation.");
        }

        _allocations.Clear();
        _allocations.AddRange(allocations.Select(a => new DonationAllocation(Id, a.FundId, decimal.Round(a.Amount, 2, MidpointRounding.AwayFromZero))));
        Total = Money.Of(_allocations.Sum(a => a.Amount), currency);
    }
}

public sealed record DonorInfo(Guid? PersonId, string? Name, string? Email);

public sealed record GiftDetails(
    DateOnly ReceivedOn, PaymentMethod Method, GivingChannel Channel, string Currency, Guid? BranchId, Guid? OccurrenceId,
    Guid? CampaignId, string? Reference, string? Notes);

/// <summary>The portion of a donation credited to one fund.</summary>
public sealed class DonationAllocation : TenantEntity
{
    private DonationAllocation() { }

    internal DonationAllocation(Guid donationId, Guid fundId, decimal amount)
    {
        DonationId = donationId;
        FundId = fundId;
        Amount = amount;
    }

    public Guid DonationId { get; private set; }
    public Guid FundId { get; private set; }
    public decimal Amount { get; private set; }
}
