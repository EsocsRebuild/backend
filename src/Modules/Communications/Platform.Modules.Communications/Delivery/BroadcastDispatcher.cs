using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Abstractions;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Communications.Domain;
using Platform.Modules.Communications.Infrastructure;
using Platform.Modules.Groups.Contracts;
using Platform.Modules.People.Contracts;
using Platform.Modules.Tenancy.Contracts;

namespace Platform.Modules.Communications.Delivery;

/// <summary>
/// Sends due broadcasts: resolves the audience once (snapshot into deliveries), then delivers in
/// batches with retries. Runs every 15 seconds; safe to resume after a crash (pending rows remain).
/// </summary>
internal sealed partial class BroadcastDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<BroadcastDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        List<(Guid TenantId, Guid Id)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            due = (await db.Broadcasts.IgnoreQueryFilters([QueryFilters.Tenant])
                    .Where(b => (b.Status == BroadcastStatus.Scheduled && b.ScheduledFor <= now) || b.Status == BroadcastStatus.Sending)
                    .Select(b => new { b.TenantId, b.Id }).ToListAsync(ct))
                .Select(x => (x.TenantId, x.Id)).ToList();
        }

        foreach (var (tenantId, id) in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
            await ProcessAsync(scope.ServiceProvider, id, ct);
        }
    }

    private async Task ProcessAsync(IServiceProvider sp, Guid broadcastId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CommunicationsDbContext>();
        var broadcast = await db.Broadcasts.FirstAsync(b => b.Id == broadcastId, ct);

        if (broadcast.Status == BroadcastStatus.Scheduled)
        {
            await ResolveAudienceAsync(sp, db, broadcast, ct);
        }

        var tenant = await sp.GetRequiredService<ITenantDirectory>().GetAsync(broadcast.TenantId, ct);
        var pending = await db.Deliveries.Where(d => d.BroadcastId == broadcastId && d.Status == DeliveryStatus.Pending)
            .OrderBy(d => d.Id).Take(BatchSize).ToListAsync(ct);

        int sent = 0, failed = 0;
        foreach (var delivery in pending)
        {
            try
            {
                await DeliverAsync(sp, db, broadcast, delivery, tenant?.Name ?? string.Empty, ct);
                delivery.MarkSent(clock.GetUtcNow());
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                delivery.MarkFailed(ex.Message, MaxAttempts);
                failed += delivery.Status == DeliveryStatus.Failed ? 1 : 0;
            }
        }

        broadcast.RecordProgress(sent, failed);
        await db.SaveChangesAsync(ct);

        if (!await db.Deliveries.AnyAsync(d => d.BroadcastId == broadcastId && d.Status == DeliveryStatus.Pending, ct))
        {
            broadcast.Complete(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task ResolveAudienceAsync(IServiceProvider sp, CommunicationsDbContext db, Broadcast broadcast, CancellationToken ct)
    {
        var spec = JsonSerializer.Deserialize<AudienceSpec>(broadcast.Audience, JsonSerializerOptions.Web) ?? new AudienceSpec();
        var personIds = spec.PersonIds?.ToList();
        if (spec.GroupId is { } groupId)
        {
            var groupMembers = await sp.GetRequiredService<IGroupDirectory>().GetMemberPersonIdsAsync(groupId, includeSubGroups: true, ct);
            personIds = personIds is { Count: > 0 } ? personIds.Intersect(groupMembers).ToList() : groupMembers.ToList();
            if (personIds.Count == 0)
            {
                personIds = [Guid.Empty]; // Empty group: match nobody rather than everybody.
            }
        }

        var contacts = await sp.GetRequiredService<IPeopleDirectory>().FindContactsAsync(
            new AudienceFilter(spec.MembershipStatuses, spec.Tags, spec.BranchId, personIds, RequireConsent: true), ct);

        var deliveries = contacts.Select(c => MessageDelivery.For(broadcast.Id, broadcast.Channel, c.PersonId, c.UserId, c.FullName, broadcast.Channel switch
        {
            Channel.Email => c.Email,
            Channel.Sms => c.PhoneNumber,
            _ => null,
        })).ToList();

        db.Deliveries.AddRange(deliveries);
        broadcast.StartSending(deliveries.Count, deliveries.Count(d => d.Status == DeliveryStatus.Skipped), clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        LogStarted(logger, broadcast.Id, deliveries.Count);
    }

    private static async Task DeliverAsync(IServiceProvider sp, CommunicationsDbContext db, Broadcast broadcast, MessageDelivery delivery, string organisation, CancellationToken ct)
    {
        var values = new Dictionary<string, string>
        {
            ["firstName"] = delivery.RecipientName.Split(' ')[0],
            ["fullName"] = delivery.RecipientName,
            ["organisation"] = organisation,
        };

        switch (broadcast.Channel)
        {
            case Channel.Email:
                await sp.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage(delivery.Destination!,
                    TemplateRenderer.Render(broadcast.Subject ?? organisation, values, htmlEncode: false),
                    TemplateRenderer.Render(broadcast.Body, values, htmlEncode: true)), ct);
                break;
            case Channel.Sms:
                await sp.GetRequiredService<ISmsSender>().SendAsync(delivery.Destination!, TemplateRenderer.Render(broadcast.Body, values, false), ct);
                break;
            case Channel.Push:
                var tokens = await db.Devices.Where(d => d.UserId == delivery.UserId).Select(d => d.Token).ToListAsync(ct);
                if (tokens.Count > 0)
                {
                    await sp.GetRequiredService<IPushSender>().SendAsync(tokens, TemplateRenderer.Render(broadcast.Subject ?? organisation, values, false),
                        TemplateRenderer.Render(broadcast.Body, values, false), null, ct);
                }

                break;
            case Channel.InApp:
                db.Notifications.Add(Notification.Create(delivery.UserId!.Value, "broadcast",
                    TemplateRenderer.Render(broadcast.Subject ?? organisation, values, false), TemplateRenderer.Render(broadcast.Body, values, false), null));
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Broadcast {BroadcastId} started for {Count} recipient(s)")]
    private static partial void LogStarted(ILogger logger, Guid broadcastId, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Broadcast dispatch failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
