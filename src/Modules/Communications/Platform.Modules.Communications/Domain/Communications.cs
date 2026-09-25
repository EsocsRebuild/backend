using Platform.SharedKernel.Domain;

namespace Platform.Modules.Communications.Domain;

public enum AnnouncementAudience
{
    /// <summary>Website, app and everyone.</summary>
    Public,

    /// <summary>Signed-in members only.</summary>
    Members,

    /// <summary>Members of one group / ministry.</summary>
    Group,
}

public enum AnnouncementStatus
{
    Draft,
    Published,
    Archived,
}

/// <summary>Church notice shown on the website, app home screen and bulletin.</summary>
public sealed class Announcement : TenantAggregateRoot
{
    private Announcement() { }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string? ImageUrl { get; private set; }
    public string? LinkUrl { get; private set; }
    public AnnouncementAudience Audience { get; private set; }
    public Guid? GroupId { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateTimeOffset PublishAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsPinned { get; private set; }
    public AnnouncementStatus Status { get; private set; }

    public static Announcement Create(string title, string body) => new() { Title = title.Trim(), Body = body, Status = AnnouncementStatus.Draft };

    public void Update(string title, string body, string? imageUrl, string? linkUrl, AnnouncementAudience audience, Guid? groupId, Guid? branchId,
        DateTimeOffset publishAt, DateTimeOffset? expiresAt, bool isPinned)
    {
        if (audience == AnnouncementAudience.Group && groupId is null)
        {
            throw new DomainException("A group announcement needs a group.");
        }

        if (expiresAt is { } exp && exp <= publishAt)
        {
            throw new DomainException("The announcement must expire after it is published.");
        }

        Title = title.Trim();
        Body = body;
        ImageUrl = imageUrl;
        LinkUrl = linkUrl;
        Audience = audience;
        GroupId = audience == AnnouncementAudience.Group ? groupId : null;
        BranchId = branchId;
        PublishAt = publishAt;
        ExpiresAt = expiresAt;
        IsPinned = isPinned;
    }

    public void Publish() => Status = AnnouncementStatus.Published;

    public void Archive() => Status = AnnouncementStatus.Archived;
}

public enum PrayerStatus
{
    New,
    Praying,
    Answered,
    Archived,
}

/// <summary>A prayer request submitted from the website, app or by staff. Confidential unless shared to the prayer wall.</summary>
public sealed class PrayerRequest : TenantAggregateRoot
{
    private PrayerRequest() { }

    public Guid? PersonId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string Request { get; private set; } = null!;

    /// <summary>Hide the requester's name everywhere except for staff.</summary>
    public bool IsAnonymous { get; private set; }

    /// <summary>The requester agreed to share it publicly; staff must still approve it for the wall.</summary>
    public bool ShareOnPrayerWall { get; private set; }

    public bool ApprovedForWall { get; private set; }
    public PrayerStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public int PrayedCount { get; private set; }
    public string? AnswerNote { get; private set; }

    public static PrayerRequest Submit(Guid? personId, Guid? userId, string name, string? email, string? phone, string request,
        bool isAnonymous, bool shareOnPrayerWall) => new()
    {
        PersonId = personId,
        UserId = userId,
        Name = name.Trim(),
        Email = email?.Trim().ToLowerInvariant(),
        PhoneNumber = phone,
        Request = request.Trim(),
        IsAnonymous = isAnonymous,
        ShareOnPrayerWall = shareOnPrayerWall,
        Status = PrayerStatus.New,
    };

    public void Manage(PrayerStatus status, Guid? assignedTo, bool approvedForWall, string? answerNote)
    {
        Status = status;
        AssignedToUserId = assignedTo;
        ApprovedForWall = approvedForWall && ShareOnPrayerWall;
        AnswerNote = answerNote;
    }

    public void RecordPrayer() => PrayedCount++;

    public string DisplayName => IsAnonymous ? "Anonymous" : Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Anonymous";
}

public enum Channel
{
    Email,
    Sms,
    Push,
    InApp,
}

/// <summary>Reusable message with {{placeholders}}: firstName, fullName, organisation.</summary>
public sealed class MessageTemplate : TenantAggregateRoot
{
    private MessageTemplate() { }

    public string Name { get; private set; } = null!;
    public Channel Channel { get; private set; }
    public string? Subject { get; private set; }
    public string Body { get; private set; } = null!;

    public static MessageTemplate Create(string name, Channel channel, string? subject, string body) =>
        new() { Name = name.Trim(), Channel = channel, Subject = subject, Body = body };

    public void Update(string name, Channel channel, string? subject, string body)
    {
        Name = name.Trim();
        Channel = channel;
        Subject = subject;
        Body = body;
    }
}

public enum BroadcastStatus
{
    Draft,
    Scheduled,
    Sending,
    Sent,
    Cancelled,
}

/// <summary>Who a broadcast targets. Criteria combine with AND; people must have consented to contact.</summary>
public sealed record AudienceSpec
{
    public IReadOnlyList<string>? MembershipStatuses { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? GroupId { get; init; }
    public IReadOnlyList<Guid>? PersonIds { get; init; }
}

/// <summary>
/// A bulk message (email / SMS / push / in-app) to a targeted audience. Recipients are resolved and
/// snapshotted into <see cref="MessageDelivery"/> rows when sending starts, then delivered in batches.
/// </summary>
public sealed class Broadcast : TenantAggregateRoot
{
    private Broadcast() { }

    public Channel Channel { get; private set; }
    public string? Subject { get; private set; }
    public string Body { get; private set; } = null!;

    /// <summary>Serialized <see cref="AudienceSpec"/>.</summary>
    public string Audience { get; private set; } = "{}";

    public BroadcastStatus Status { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int RecipientCount { get; private set; }
    public int SentCount { get; private set; }
    public int FailedCount { get; private set; }
    public int SkippedCount { get; private set; }

    public static Broadcast Create(Channel channel, string? subject, string body, string audienceJson) =>
        new() { Channel = channel, Subject = subject, Body = body, Audience = audienceJson, Status = BroadcastStatus.Draft };

    public void Update(Channel channel, string? subject, string body, string audienceJson)
    {
        EnsureDraft();
        Channel = channel;
        Subject = subject;
        Body = body;
        Audience = audienceJson;
    }

    public void Schedule(DateTimeOffset at)
    {
        EnsureDraft();
        Status = BroadcastStatus.Scheduled;
        ScheduledFor = at;
    }

    public void Cancel()
    {
        if (Status is BroadcastStatus.Sent or BroadcastStatus.Sending)
        {
            throw new DomainException("A broadcast that has started sending cannot be cancelled.");
        }

        Status = BroadcastStatus.Cancelled;
    }

    public void StartSending(int recipients, int skipped, DateTimeOffset now)
    {
        Status = BroadcastStatus.Sending;
        StartedAt = now;
        RecipientCount = recipients;
        SkippedCount = skipped;
    }

    public void RecordProgress(int sent, int failed)
    {
        SentCount += sent;
        FailedCount += failed;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = BroadcastStatus.Sent;
        CompletedAt = now;
    }

    private void EnsureDraft()
    {
        if (Status != BroadcastStatus.Draft)
        {
            throw new DomainException("Only draft broadcasts can be changed.");
        }
    }
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,
}

public sealed class MessageDelivery : TenantEntity
{
    private MessageDelivery() { }

    public Guid BroadcastId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid? UserId { get; private set; }
    public string RecipientName { get; private set; } = null!;
    public string? Destination { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public static MessageDelivery For(Guid broadcastId, Channel channel, Guid personId, Guid? userId, string name, string? destination)
    {
        var reachable = channel is Channel.Email or Channel.Sms ? !string.IsNullOrWhiteSpace(destination) : userId is not null;
        return new MessageDelivery
        {
            BroadcastId = broadcastId,
            PersonId = personId,
            UserId = userId,
            RecipientName = name,
            Destination = destination,
            Status = reachable ? DeliveryStatus.Pending : DeliveryStatus.Skipped,
            Error = reachable ? null : $"No {channel} contact for this person.",
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = DeliveryStatus.Sent;
        SentAt = now;
        Error = null;
    }

    public void MarkFailed(string error, int maxAttempts)
    {
        Attempts++;
        Error = error.Length > 500 ? error[..500] : error;
        Status = Attempts >= maxAttempts ? DeliveryStatus.Failed : DeliveryStatus.Pending;
    }
}

public enum DevicePlatform
{
    Ios,
    Android,
    Web,
}

/// <summary>A mobile/web push token registered by a signed-in user.</summary>
public sealed class DeviceRegistration : TenantAggregateRoot
{
    private DeviceRegistration() { }

    public Guid UserId { get; private set; }
    public DevicePlatform Platform { get; private set; }
    public string Token { get; private set; } = null!;
    public string? AppVersion { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    public static DeviceRegistration Register(Guid userId, DevicePlatform platform, string token, string? appVersion, DateTimeOffset now) =>
        new() { UserId = userId, Platform = platform, Token = token, AppVersion = appVersion, LastSeenAt = now };

    public void Refresh(Guid userId, DevicePlatform platform, string? appVersion, DateTimeOffset now)
    {
        UserId = userId;
        Platform = platform;
        AppVersion = appVersion;
        LastSeenAt = now;
    }
}

/// <summary>In-app notification inbox item for a user.</summary>
public sealed class Notification : TenantEntity
{
    private Notification() { }

    public Guid UserId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string? Link { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public static Notification Create(Guid userId, string category, string title, string body, string? link) =>
        new() { UserId = userId, Category = category, Title = title, Body = body, Link = link };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;
}
