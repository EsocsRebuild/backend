using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Platform.Application.Messaging;
using Platform.SharedKernel.Domain;

namespace Platform.Infrastructure.Outbox;

/// <summary>Resolves and invokes all <see cref="IEventHandler{TEvent}"/> for an event, sequentially.</summary>
internal sealed class InProcessEventDispatcher(IServiceProvider serviceProvider) : IEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Invokers = new();

    public Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken) =>
        Invokers.GetOrAdd(@event.GetType(), CreateInvoker)(serviceProvider, @event, cancellationToken);

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> CreateInvoker(Type eventType) =>
        (Func<IServiceProvider, IDomainEvent, CancellationToken, Task>)typeof(InProcessEventDispatcher)
            .GetMethod(nameof(InvokeAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(eventType)
            .CreateDelegate(typeof(Func<IServiceProvider, IDomainEvent, CancellationToken, Task>));

    private static async Task InvokeAsync<TEvent>(IServiceProvider provider, IDomainEvent @event, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in provider.GetServices<IEventHandler<TEvent>>())
        {
            await handler.Handle((TEvent)@event, cancellationToken);
        }

        var publisher = provider.GetService<MediatR.IPublisher>();
        if (publisher is not null)
        {
            await publisher.Publish(@event, cancellationToken);
        }
    }
}
