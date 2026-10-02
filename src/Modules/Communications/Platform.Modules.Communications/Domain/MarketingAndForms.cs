using System.Text.Json;
using Platform.SharedKernel.Domain;

namespace Platform.Modules.Communications.Domain;

public sealed class AudienceList : TenantAggregateRoot
{
    private AudienceList() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool DoubleOptIn { get; private set; }
    public int SubscriberCount { get; internal set; }
    public int UnsubscribedCount { get; internal set; }

    public static AudienceList Create(string name, string? description, bool doubleOptIn) => new()
    {
        Name = name.Trim(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        DoubleOptIn = doubleOptIn,
    };

    public void Update(string name, string? description, bool doubleOptIn)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DoubleOptIn = doubleOptIn;
    }
}

public sealed class AudienceContact : TenantEntity
{
    private AudienceContact() { }

    public Guid ListId { get; private set; }
    public string Email { get; private set; } = null!;
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string Status { get; private set; } = "subscribed"; // subscribed, pending, unsubscribed, bounced, complained
    public string Source { get; private set; } = "import";     // import, form, member, manual, api
    public Guid? MemberId { get; private set; }
    public DateTimeOffset? SubscribedAt { get; private set; }

    public static AudienceContact Create(Guid listId, string email, string? firstName, string? lastName,
        string status, string source, Guid? memberId, DateTimeOffset now) => new()
    {
        ListId = listId,
        Email = email.Trim().ToLowerInvariant(),
        FirstName = string.IsNullOrWhiteSpace(firstName) ? null : firstName.Trim(),
        LastName = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim(),
        Status = status,
        Source = source,
        MemberId = memberId,
        SubscribedAt = status == "subscribed" ? now : null,
    };

    public void Unsubscribe()
    {
        Status = "unsubscribed";
    }

    public void MarkBounced()
    {
        Status = "bounced";
    }
}

public sealed class EmailTemplate : TenantAggregateRoot
{
    private EmailTemplate() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string ContentJson { get; private set; } = "{}";
    public Guid? UpdatedById { get; private set; }
    public string? UpdatedByName { get; private set; }

    public static EmailTemplate Create(string name, string? description, string contentJson, Guid? updatedBy, string? updatedByName) => new()
    {
        Name = name.Trim(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        ContentJson = contentJson,
        UpdatedById = updatedBy,
        UpdatedByName = updatedByName,
    };

    public void Update(string name, string? description, string contentJson, Guid? updatedBy, string? updatedByName)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ContentJson = contentJson;
        UpdatedById = updatedBy;
        UpdatedByName = updatedByName;
    }
}

public sealed class EmailCampaign : TenantAggregateRoot
{
    private EmailCampaign() { }

    public string Name { get; private set; } = null!;
    public string? Subject { get; private set; }
    public string? PreviewText { get; private set; }
    public string? FromName { get; private set; }
    public string? FromEmail { get; private set; }
    public string? ReplyTo { get; private set; }
    public string Status { get; private set; } = "draft"; // draft, scheduled, sending, sent, paused, cancelled, failed
    public int? RecipientCount { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? StatsJson { get; private set; }
    public string AudienceJson { get; private set; } = "{\"listIds\":[]}";
    public string ContentJson { get; private set; } = "{}";
    public Guid? CreatedById { get; private set; }
    public string? CreatedByName { get; private set; }

    public static EmailCampaign Create(string name, string? subject, string contentJson, string? audienceJson,
        Guid? createdBy, string? createdByName) => new()
    {
        Name = name.Trim(),
        Subject = subject,
        ContentJson = contentJson,
        AudienceJson = audienceJson ?? "{\"listIds\":[]}",
        Status = "draft",
        CreatedById = createdBy,
        CreatedByName = createdByName,
    };

    public void UpdateDraft(string? name, string? subject, string? previewText, string? fromName, string? fromEmail,
        string? replyTo, string? audienceJson, string? contentJson)
    {
        if (Status != "draft")
        {
            throw new DomainException("Only draft campaigns can be updated.");
        }

        if (!string.IsNullOrWhiteSpace(name)) Name = name.Trim();
        if (subject != null) Subject = subject.Trim();
        if (previewText != null) PreviewText = previewText.Trim();
        if (fromName != null) FromName = fromName.Trim();
        if (fromEmail != null) FromEmail = fromEmail.Trim();
        if (replyTo != null) ReplyTo = replyTo.Trim();
        if (audienceJson != null) AudienceJson = audienceJson;
        if (contentJson != null) ContentJson = contentJson;
    }

    public void Schedule(DateTimeOffset sendAt, int recipientCount)
    {
        if (Status != "draft")
        {
            throw new DomainException("Only draft campaigns can be scheduled.");
        }

        Status = "scheduled";
        ScheduledAt = sendAt;
        RecipientCount = recipientCount;
    }

    public void Unschedule()
    {
        if (Status != "scheduled")
        {
            throw new DomainException("Only scheduled campaigns can be unscheduled.");
        }

        Status = "draft";
        ScheduledAt = null;
    }

    public void MarkSent(int recipientCount, string statsJson, DateTimeOffset now)
    {
        Status = "sent";
        SentAt = now;
        RecipientCount = recipientCount;
        StatsJson = statsJson;
    }
}

public sealed class SendingSettings : TenantAggregateRoot
{
    private SendingSettings() { }

    public string? DefaultFromName { get; private set; }
    public string? DefaultReplyTo { get; private set; }
    public string OrganisationName { get; private set; } = "ESOCS";
    public string? PostalAddress { get; private set; }

    public static SendingSettings Create(string orgName, string? fromName, string? replyTo, string? postalAddress) => new()
    {
        OrganisationName = orgName,
        DefaultFromName = fromName,
        DefaultReplyTo = replyTo,
        PostalAddress = postalAddress,
    };

    public void Update(string orgName, string? fromName, string? replyTo, string? postalAddress)
    {
        OrganisationName = orgName;
        DefaultFromName = fromName;
        DefaultReplyTo = replyTo;
        PostalAddress = postalAddress;
    }
}

public sealed class SendingDomain : TenantAggregateRoot
{
    private SendingDomain() { }

    public string Domain { get; private set; } = null!;
    public string Status { get; private set; } = "pending"; // pending, verified, failed
    public string RecordsJson { get; private set; } = "[]";
    public DateTimeOffset? LastCheckedAt { get; private set; }

    public static SendingDomain Create(string domain, string recordsJson) => new()
    {
        Domain = domain.Trim().ToLowerInvariant(),
        Status = "pending",
        RecordsJson = recordsJson,
    };

    public void Verify(DateTimeOffset now)
    {
        Status = "verified";
        LastCheckedAt = now;
    }
}

public sealed class FormDefinition : TenantAggregateRoot
{
    private FormDefinition() { }

    public string Title { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Status { get; private set; } = "draft"; // draft, published, closed, archived
    public string FieldsJson { get; private set; } = "[]";
    public string SettingsJson { get; private set; } = "{}";
    public DateTimeOffset? PublishedAt { get; private set; }

    public static FormDefinition Create(string title, string slug, string? description, string fieldsJson, string settingsJson) => new()
    {
        Title = title.Trim(),
        Slug = slug.Trim().ToLowerInvariant(),
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        Status = "draft",
        FieldsJson = fieldsJson,
        SettingsJson = settingsJson,
    };

    public void Update(string? title, string? description, string? fieldsJson)
    {
        if (!string.IsNullOrWhiteSpace(title)) Title = title.Trim();
        if (description != null) Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (fieldsJson != null) FieldsJson = fieldsJson;
    }

    public void UpdateSettings(string settingsJson)
    {
        SettingsJson = settingsJson;
    }

    public void UpdateSlug(string slug)
    {
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void Publish(DateTimeOffset now)
    {
        Status = "published";
        PublishedAt ??= now;
    }

    public void Close()
    {
        Status = "closed";
    }

    public void Archive()
    {
        Status = "archived";
    }
}

public sealed class FormResponseEntry : TenantEntity
{
    private FormResponseEntry() { }

    public Guid FormId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string AnswersJson { get; private set; } = "{}";

    public static FormResponseEntry Create(Guid formId, string answersJson, DateTimeOffset now) => new()
    {
        FormId = formId,
        AnswersJson = answersJson,
        SubmittedAt = now,
    };
}
