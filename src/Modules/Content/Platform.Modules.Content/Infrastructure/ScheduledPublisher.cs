using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Content.Domain;

namespace Platform.Modules.Content.Infrastructure;

/// <summary>Publishes scheduled pages, posts and sermons when their time arrives (checks every minute).</summary>
internal sealed partial class ScheduledPublisher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ScheduledPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await PublishDueAsync<Page>(stoppingToken);
                await PublishDueAsync<Post>(stoppingToken);
                await PublishDueAsync<Sermon>(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PublishDueAsync<T>(CancellationToken ct) where T : PublishableContent
    {
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContentDbContext>();
        var due = await db.Set<T>().IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(x => x.Status == ContentStatus.Scheduled && x.ScheduledFor <= now)
            .OrderBy(x => x.ScheduledFor)
            .Take(200).ToListAsync(ct);

        foreach (var tenantGroup in due.GroupBy(x => x.TenantId))
        {
            // Writes are tenant-checked: bind each group to its own tenant.
            await using var tenantScope = scopes.CreateAsyncScope();
            tenantScope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantGroup.Key);
            var tenantDb = tenantScope.ServiceProvider.GetRequiredService<ContentDbContext>();
            var ids = tenantGroup.Select(x => x.Id).ToList();
            var items = await tenantDb.Set<T>().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
            items.ForEach(i => i.Publish(now));
            await tenantDb.SaveChangesAsync(ct);
            LogPublished(logger, items.Count, typeof(T).Name, tenantGroup.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {Count} scheduled {Type} item(s) for tenant {TenantId}")]
    private static partial void LogPublished(ILogger logger, int count, string type, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled publishing failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
