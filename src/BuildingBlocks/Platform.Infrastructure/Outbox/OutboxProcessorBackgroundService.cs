using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Infrastructure.Modules;
using Platform.Infrastructure.Persistence;

namespace Platform.Infrastructure.Outbox;

/// <summary>
/// Background worker that processes unpublished integration events from the outbox_messages table
/// every 250ms with batch locking (FOR UPDATE SKIP LOCKED).
/// </summary>
public sealed class OutboxProcessorBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessorBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var moduleDatabases = scope.ServiceProvider.GetServices<IModuleDatabase>();

                foreach (var moduleDb in moduleDatabases)
                {
                    if (scope.ServiceProvider.GetService(moduleDb.ContextType) is ModuleDbContext dbContext)
                    {
                        // Drain up to BatchSize messages with SKIP LOCKED
                        await ProcessModuleOutboxAsync(scope.ServiceProvider, dbContext, stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing outbox messages across module databases.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessModuleOutboxAsync(IServiceProvider serviceProvider, ModuleDbContext context, CancellationToken cancellationToken)
    {
        // Handled securely via module execution strategy
        try
        {
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

#pragma warning disable EF1002 // Schema is a compile-time constant owned by module
                var messages = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    context.OutboxMessages.FromSqlRaw($$"""
                        SELECT * FROM "{{context.Schema}}".outbox_messages
                        WHERE processed_at IS NULL AND attempts < {0}
                        ORDER BY occurred_at
                        LIMIT {1}
                        FOR UPDATE SKIP LOCKED
                        """, options.Value.MaxAttempts, options.Value.BatchSize),
                    cancellationToken);
#pragma warning restore EF1002

                if (messages.Count == 0)
                {
                    return;
                }

                foreach (var message in messages)
                {
                    message.Attempts++;
                    try
                    {
                        var type = Type.GetType(message.Type, throwOnError: false);
                        if (type is not null && System.Text.Json.JsonSerializer.Deserialize(
                            message.Content, type, Platform.Infrastructure.Persistence.Interceptors.PlatformSaveChangesInterceptor.EventSerializerOptions) is Platform.SharedKernel.Domain.IDomainEvent domainEvent)
                        {
                            await using var handlerScope = scopeFactory.CreateAsyncScope();
                            handlerScope.ServiceProvider.GetRequiredService<Platform.Application.Tenancy.ITenantContextSetter>().SetTenant(message.TenantId);
                            await handlerScope.ServiceProvider.GetRequiredService<Platform.Application.Messaging.IEventDispatcher>().DispatchAsync(domainEvent, cancellationToken);
                            message.ProcessedAt = DateTimeOffset.UtcNow;
                            message.Error = null;
                        }
                        else
                        {
                            message.Error = $"Unknown or deserialization failed for event type '{message.Type}'.";
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        message.Error = ex.ToString()[..Math.Min(ex.ToString().Length, 4000)];
                        logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed on attempt {Attempt}", message.Id, message.Type, message.Attempts);
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to claim and dispatch outbox batch for schema {Schema}", context.Schema);
        }
    }
}
