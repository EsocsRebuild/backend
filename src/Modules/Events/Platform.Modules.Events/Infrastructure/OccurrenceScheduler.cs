using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Modules.Events.Domain;

namespace Platform.Modules.Events.Infrastructure;

/// <summary>Keeps recurring events materialised for the scheduling horizon (runs hourly).</summary>
internal sealed partial class OccurrenceScheduler(IServiceScopeFactory scopes, ILogger<OccurrenceScheduler> logger) : BackgroundService
{
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(120);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
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

    private async Task RunAsync(CancellationToken ct)
    {
        List<(Guid TenantId, Guid EventId)> recurring;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EventsDbContext>();
            recurring = (await db.Events.IgnoreQueryFilters([QueryFilters.Tenant])
                .Where(e => e.RecurrenceRule != null && e.Status == EventStatus.Published)
                .Select(e => new { e.TenantId, e.Id }).ToListAsync(ct))
                .Select(x => (x.TenantId, x.Id)).ToList();
        }

        foreach (var (tenantId, eventId) in recurring)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenantId);
            var sync = scope.ServiceProvider.GetRequiredService<OccurrenceSync>();
            await sync.SyncAsync(eventId, ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Occurrence scheduling failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Re-materialises an event's occurrences, protecting any that already have registrations or attendance.</summary>
internal sealed class OccurrenceSync(EventsDbContext db, TimeProvider clock)
{
    public async Task SyncAsync(Guid eventId, CancellationToken ct)
    {
        var entity = await db.Events.Include(e => e.Occurrences).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (entity is null)
        {
            return;
        }

        await SyncAsync(entity, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task SyncAsync(Event entity, CancellationToken ct)
    {
        var ids = entity.Occurrences.Select(o => o.Id).ToList();
        var active = await db.Registrations.Where(r => ids.Contains(r.OccurrenceId)).Select(r => r.OccurrenceId)
            .Union(db.Attendance.Where(a => ids.Contains(a.OccurrenceId)).Select(a => a.OccurrenceId))
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        entity.SyncOccurrences(now, now.Add(OccurrenceScheduler.Horizon), active.ToHashSet());
    }
}
