namespace Platform.SharedKernel.Domain;

/// <summary>An event raised inside a module. Handled asynchronously via the outbox.</summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An event published by one module for other modules to consume. Integration events are
/// part of a module's public contract (its *.Contracts project) and must stay backward compatible.
/// </summary>
public interface IIntegrationEvent : IDomainEvent
{
    Guid TenantId { get; }
}

public abstract record IntegrationEvent(Guid TenantId) : DomainEvent, IIntegrationEvent;
