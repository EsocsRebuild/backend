using Platform.SharedKernel.Domain;

namespace Platform.Modules.Giving.Domain;

/// <summary>A designated purpose money is given to: Tithe, Offering, Building, Missions, Welfare…</summary>
public sealed class Fund : TenantAggregateRoot
{
    private Fund() { }

    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsTaxDeductible { get; private set; } = true;

    /// <summary>Offered to givers on the website and app.</summary>
    public bool IsPublic { get; private set; } = true;

    public int SortOrder { get; private set; }

    public static Fund Create(string name, string code, string? description, bool isPublic, int sortOrder) =>
        new() { Name = name.Trim(), Code = code.ToUpperInvariant(), Description = description, IsPublic = isPublic, SortOrder = sortOrder };

    public void Update(string name, string code, string? description, bool isActive, bool isTaxDeductible, bool isPublic, int sortOrder)
    {
        Name = name.Trim();
        Code = code.ToUpperInvariant();
        Description = description;
        IsActive = isActive;
        IsTaxDeductible = isTaxDeductible;
        IsPublic = isPublic;
        SortOrder = sortOrder;
    }
}

/// <summary>A fundraising drive with a goal (e.g. "New Sanctuary 2027"), optionally tied to a fund.</summary>
public sealed class Campaign : TenantAggregateRoot
{
    private Campaign() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid FundId { get; private set; }
    public Money Goal { get; private set; } = null!;
    public DateOnly StartsOn { get; private set; }
    public DateOnly? EndsOn { get; private set; }
    public bool IsActive { get; private set; } = true;

    public static Campaign Create(string name, Guid fundId, Money goal, DateOnly startsOn) =>
        new() { Name = name.Trim(), FundId = fundId, Goal = goal, StartsOn = startsOn };

    public void Update(string name, string? description, Guid fundId, Money goal, DateOnly startsOn, DateOnly? endsOn, bool isActive)
    {
        if (endsOn is { } end && end < startsOn)
        {
            throw new DomainException("A campaign cannot end before it starts.");
        }

        Name = name.Trim();
        Description = description;
        FundId = fundId;
        Goal = goal;
        StartsOn = startsOn;
        EndsOn = endsOn;
        IsActive = isActive;
    }
}

public enum PledgeFrequency
{
    OneTime,
    Weekly,
    Monthly,
    Quarterly,
    Annually,
}

public enum PledgeStatus
{
    Active,
    Fulfilled,
    Cancelled,
}

/// <summary>A person's commitment towards a campaign. Progress is computed from their completed gifts to it.</summary>
public sealed class Pledge : TenantAggregateRoot
{
    private Pledge() { }

    public Guid CampaignId { get; private set; }
    public Guid PersonId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PledgeFrequency Frequency { get; private set; }
    public DateOnly PledgedOn { get; private set; }
    public PledgeStatus Status { get; private set; }
    public string? Notes { get; private set; }

    public static Pledge Create(Guid campaignId, Guid personId, Money amount, PledgeFrequency frequency, DateOnly pledgedOn, string? notes) =>
        new() { CampaignId = campaignId, PersonId = personId, Amount = amount, Frequency = frequency, PledgedOn = pledgedOn, Notes = notes, Status = PledgeStatus.Active };

    public void Update(Money amount, PledgeFrequency frequency, PledgeStatus status, string? notes)
    {
        Amount = amount;
        Frequency = frequency;
        Status = status;
        Notes = notes;
    }
}

public enum BatchStatus
{
    Open,
    Closed,
}

/// <summary>
/// A counting batch — e.g. "Sunday 1st service offering" — so counters can reconcile the
/// counted cash/cheques against the expected total before the batch is closed and locked.
/// </summary>
public sealed class DonationBatch : TenantAggregateRoot
{
    private DonationBatch() { }

    public string Name { get; private set; } = null!;
    public DateOnly BatchDate { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? OccurrenceId { get; private set; }
    public string Currency { get; private set; } = null!;

    /// <summary>Total the counters declared (e.g. from counting sheets), to reconcile against recorded gifts.</summary>
    public decimal? ExpectedTotal { get; private set; }

    public BatchStatus Status { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }

    public static DonationBatch Open(string name, DateOnly date, string currency, Guid? branchId, Guid? occurrenceId, decimal? expectedTotal) => new()
    {
        Name = name.Trim(),
        BatchDate = date,
        Currency = currency.ToUpperInvariant(),
        BranchId = branchId,
        OccurrenceId = occurrenceId,
        ExpectedTotal = expectedTotal,
        Status = BatchStatus.Open,
    };

    public void Update(string name, DateOnly date, decimal? expectedTotal)
    {
        EnsureOpen();
        Name = name.Trim();
        BatchDate = date;
        ExpectedTotal = expectedTotal;
    }

    public void Close(IEnumerable<Donation> donations, DateTimeOffset now, Guid? userId)
    {
        EnsureOpen();
        foreach (var donation in donations)
        {
            donation.Lock();
        }

        Status = BatchStatus.Closed;
        ClosedAt = now;
        ClosedBy = userId;
    }

    public void Reopen(IEnumerable<Donation> donations)
    {
        foreach (var donation in donations)
        {
            donation.Unlock();
        }

        Status = BatchStatus.Open;
        ClosedAt = null;
        ClosedBy = null;
    }

    public void EnsureOpen()
    {
        if (Status != BatchStatus.Open)
        {
            throw new DomainException("The batch is closed.");
        }
    }
}
