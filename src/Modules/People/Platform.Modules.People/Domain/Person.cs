using Platform.Modules.People.Contracts;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.People.Domain;

public enum Gender
{
    Unspecified,
    Male,
    Female,
}

public enum MaritalStatus
{
    Unspecified,
    Single,
    Married,
    Widowed,
    Divorced,
    Separated,
}

/// <summary>Where a person is in their journey with the church. Transitions are recorded in history.</summary>
public enum MembershipStatus
{
    Visitor,
    Regular,
    Member,
    Inactive,
    Transferred,
    Deceased,
}

/// <summary>State of the record in the admin portal (contract <c>MemberStatus</c>).</summary>
public enum RecordStatus
{
    Active,

    /// <summary>Awaiting approval (e.g. signed up on the website or app).</summary>
    Pending,
    Inactive,
    Suspended,
}

public enum HouseholdRole
{
    Head,
    Spouse,
    Child,
    Dependant,
    Other,
}

/// <summary>
/// A person known to the organisation — visitor, regular attender, member, child. Not every person
/// has a login; <see cref="UserId"/> links the profile to an Identity account when they do.
/// </summary>
public sealed class Person : TenantAggregateRoot
{
    private readonly List<MembershipStatusChange> _statusHistory = [];

    private Person() { }

    /// <summary>Human-friendly, per-organisation unique identifier printed on cards and reports, e.g. "MBR-000123".</summary>
    public string MemberNumber { get; private set; } = null!;

    public RecordStatus Status { get; private set; }

    /// <summary>Rank / title in the church (e.g. "Evangelist", "Senior Apostle").</summary>
    public string? Rank { get; private set; }

    /// <summary>Administrative notes on the record (pastoral notes live in <see cref="PersonNote"/>).</summary>
    public string? Notes { get; private set; }

    /// <summary>Agreed to receive email updates. Required before being added to any email audience.</summary>
    public bool EmailConsent { get; private set; }

    public DateTimeOffset? EmailConsentRecordedAt { get; private set; }
    public Guid? EmailConsentRecordedBy { get; private set; }

    /// <summary>How consent was obtained: "admin", "website", "form", "import".</summary>
    public string? EmailConsentSource { get; private set; }

    public Guid? UnitId { get; private set; }
    public Guid? HouseholdId { get; private set; }
    public HouseholdRole? HouseholdRole { get; private set; }
    public Guid? UserId { get; private set; }

    public string? Title { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string? MiddleName { get; private set; }
    public string LastName { get; private set; } = null!;
    public string? PreferredName { get; private set; }
    public Gender Gender { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public MaritalStatus MaritalStatus { get; private set; }
    public DateOnly? WeddingAnniversary { get; private set; }

    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? AlternatePhoneNumber { get; private set; }
    public Address Address { get; private set; } = Address.Empty;
    public string? Occupation { get; private set; }
    public string? Employer { get; private set; }
    public string? PhotoUrl { get; private set; }

    public MembershipStatus MembershipStatus { get; private set; }
    public DateOnly? FirstVisitDate { get; private set; }
    public DateOnly? MembershipDate { get; private set; }
    public DateOnly? SalvationDate { get; private set; }
    public DateOnly? BaptismDate { get; private set; }

    /// <summary>How the person heard about the church (invitation, online, event…).</summary>
    public string? Source { get; private set; }

    /// <summary>Explicit consent to receive email/SMS/push communications (data-protection compliance).</summary>
    public bool ConsentToContact { get; private set; }

    /// <summary>Free-form segmentation labels, e.g. "choir", "new-convert".</summary>
    public List<string> Tags { get; private set; } = [];

    /// <summary>Values for tenant-defined custom fields, as a JSON object keyed by field key.</summary>
    public string CustomFields { get; private set; } = "{}";

    public IReadOnlyCollection<MembershipStatusChange> StatusHistory => _statusHistory.AsReadOnly();

    public string FullName => $"{PreferredName ?? FirstName} {LastName}";

    public static Person Create(Guid tenantId, string memberNumber, string firstName, string lastName, MembershipStatus status, DateOnly today)
    {
        var person = new Person
        {
            MemberNumber = memberNumber,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            MembershipStatus = status,
            Status = RecordStatus.Active,
            FirstVisitDate = today,
            MembershipDate = status == MembershipStatus.Member ? today : null,
        };

        person.AssignTenant(tenantId);
        person.Raise(new PersonCreatedIntegrationEvent(tenantId, person.Id, person.FullName, status.ToString(), null));
        return person;
    }

    public void UpdateProfile(PersonProfile p)
    {
        Title = p.Title;
        FirstName = p.FirstName.Trim();
        MiddleName = p.MiddleName;
        LastName = p.LastName.Trim();
        PreferredName = p.PreferredName;
        Gender = p.Gender;
        DateOfBirth = p.DateOfBirth;
        MaritalStatus = p.MaritalStatus;
        WeddingAnniversary = p.WeddingAnniversary;
        Email = p.Email?.Trim().ToLowerInvariant();
        PhoneNumber = p.PhoneNumber;
        AlternatePhoneNumber = p.AlternatePhoneNumber;
        Address = p.Address ?? Address.Empty;
        Occupation = p.Occupation;
        Employer = p.Employer;
        PhotoUrl = p.PhotoUrl;
        Source = p.Source;
        ConsentToContact = p.ConsentToContact;
        SalvationDate = p.SalvationDate;
        BaptismDate = p.BaptismDate;
        FirstVisitDate = p.FirstVisitDate ?? FirstVisitDate;
        UnitId = p.UnitId;
        Tags = (p.Tags ?? []).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
    }

    /// <summary>Self-service edits from the website / mobile app — a restricted subset of fields.</summary>
    public void UpdateContactDetails(string? phoneNumber, string? alternatePhone, Address? address, string? occupation, string? photoUrl, bool consentToContact)
    {
        PhoneNumber = phoneNumber;
        AlternatePhoneNumber = alternatePhone;
        Address = address ?? Address;
        Occupation = occupation;
        PhotoUrl = photoUrl;
        ConsentToContact = consentToContact;
    }

    public void SetCustomFields(string json) => CustomFields = json;

    /// <summary>Fields edited from the admin portal's member form.</summary>
    public void UpdateRecord(string firstName, string lastName, string? email, string? phone, Guid? unitId, string? rank, Gender gender,
        DateOnly? dateOfBirth, string? address, string? notes, RecordStatus status)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UnitId = unitId;
        Rank = string.IsNullOrWhiteSpace(rank) ? null : rank.Trim();
        Gender = gender;
        DateOfBirth = dateOfBirth;
        Address = string.IsNullOrWhiteSpace(address) ? Address.Empty : Address with { Line1 = address.Trim() };
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = status;
    }

    public void SetStatus(RecordStatus status) => Status = status;

    /// <summary>Records (or withdraws) consent to email, with who attested it and how (anti-spam compliance).</summary>
    public void SetEmailConsent(bool consent, Guid? recordedBy, string source, DateTimeOffset now)
    {
        if (consent == EmailConsent)
        {
            return;
        }

        EmailConsent = consent;
        EmailConsentRecordedAt = now;
        EmailConsentRecordedBy = recordedBy;
        EmailConsentSource = source;
    }

    public void ChangeMembershipStatus(MembershipStatus status, DateOnly effectiveDate, string? reason)
    {
        if (status == MembershipStatus)
        {
            return;
        }

        var previous = MembershipStatus;
        _statusHistory.Add(new MembershipStatusChange(Id, previous, status, effectiveDate, reason));
        MembershipStatus = status;

        if (status == MembershipStatus.Member)
        {
            MembershipDate ??= effectiveDate;
        }

        Raise(new PersonMembershipStatusChangedIntegrationEvent(TenantId, Id, previous.ToString(), status.ToString()));
    }

    public void LinkAccount(Guid userId, Guid membershipId)
    {
        if (UserId == userId)
        {
            return;
        }

        UserId = userId;
        Raise(new PersonLinkedToAccountIntegrationEvent(TenantId, Id, userId, membershipId));
    }

    public void JoinHousehold(Guid householdId, HouseholdRole role)
    {
        HouseholdId = householdId;
        HouseholdRole = role;
    }

    public void LeaveHousehold()
    {
        HouseholdId = null;
        HouseholdRole = null;
    }
}

public sealed record PersonProfile(
    string? Title, string FirstName, string? MiddleName, string LastName, string? PreferredName, Gender Gender,
    DateOnly? DateOfBirth, MaritalStatus MaritalStatus, DateOnly? WeddingAnniversary, string? Email, string? PhoneNumber,
    string? AlternatePhoneNumber, Address? Address, string? Occupation, string? Employer, string? PhotoUrl, string? Source,
    bool ConsentToContact, DateOnly? SalvationDate, DateOnly? BaptismDate, DateOnly? FirstVisitDate, Guid? UnitId,
    IReadOnlyList<string>? Tags);

/// <summary>Immutable history of membership status transitions.</summary>
public sealed class MembershipStatusChange : TenantEntity
{
    private MembershipStatusChange() { }

    internal MembershipStatusChange(Guid personId, MembershipStatus from, MembershipStatus to, DateOnly effectiveDate, string? reason)
    {
        PersonId = personId;
        From = from;
        To = to;
        EffectiveDate = effectiveDate;
        Reason = reason;
    }

    public Guid PersonId { get; private set; }
    public MembershipStatus From { get; private set; }
    public MembershipStatus To { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string? Reason { get; private set; }
}
