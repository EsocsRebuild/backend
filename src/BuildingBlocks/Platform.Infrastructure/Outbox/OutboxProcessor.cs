using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Application.Messaging;
using Platform.Application.Tenancy;
using Platform.Infrastructure.Persistence;
using Platform.Infrastructure.Persistence.Interceptors;
using Platform.SharedKernel.Domain;

namespace Platform.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>
/// Delivers pending outbox messages of one module. Safe to run on many instances concurrently:
/// rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>. Failed messages are retried with
/// the attempt count and last error recorded; after MaxAttempts they stay for inspection.
/// </summary>
public sealed partial class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : ModuleDbContext
{
    private static readonly ConcurrentDictionary<string, Type?> TypeCache = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval);
        do
        {
            try
            {
                while (await ProcessBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // Keep draining while full batches come back.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, typeof(TContext).Name, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        // Retrying execution strategies require explicit transactions to run inside the strategy.
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(
            (Context: context, Processor: this),
            static (_, state, ct) => state.Processor.ClaimAndDispatchAsync(state.Context, ct),
            verifySucceeded: null,
            cancellationToken);
    }

    private async Task<int> ClaimAndDispatchAsync(TContext context, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

#pragma warning disable EF1002 // Schema is a compile-time constant owned by the module, not user input.
        var messages = await context.OutboxMessages
            .FromSqlRaw($$"""
                SELECT * FROM "{{context.Schema}}".outbox_messages
                WHERE processed_at IS NULL AND attempts < {0}
                ORDER BY occurred_at
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
                """, options.Value.MaxAttempts, options.Value.BatchSize)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002

        foreach (var message in messages)
        {
            await DispatchAsync(message, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        message.Attempts++;
        try
        {
            var type = TypeCache.GetOrAdd(message.Type, static name => Type.GetType(name, throwOnError: false))
                ?? throw new InvalidOperationException($"Unknown event type '{message.Type}'.");

            var domainEvent = (IDomainEvent)(JsonSerializer.Deserialize(message.Content, type, PlatformSaveChangesInterceptor.EventSerializerOptions)
                ?? throw new InvalidOperationException("Event payload deserialised to null."));

            // Each message is handled in its own scope, bound to the tenant it was raised in.
            await using var handlerScope = scopeFactory.CreateAsyncScope();
            handlerScope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(message.TenantId);
            await handlerScope.ServiceProvider.GetRequiredService<IEventDispatcher>().DispatchAsync(domainEvent, cancellationToken);

            message.ProcessedAt = DateTimeOffset.UtcNow;
            message.Error = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.Error = ex.ToString()[..Math.Min(ex.ToString().Length, 4000)];
            LogMessageFailed(logger, message.Id, message.Type, message.Attempts, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed for {Context}")]
    private static partial void LogBatchFailed(ILogger logger, string context, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed on attempt {Attempt}")]
    private static partial void LogMessageFailed(ILogger logger, Guid messageId, string type, int attempt, Exception exception);
}
