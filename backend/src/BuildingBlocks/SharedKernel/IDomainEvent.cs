namespace LupexWallet.SharedKernel;

/// <summary>
/// Domain event marker (ddd-model.md, §6). Has no dependency on MediatR — the Domain
/// layer must not know about the delivery mechanism; BuildingBlocks.Infrastructure builds
/// the MediatR.INotification wrapper (see DomainEventNotification&lt;T&gt;).
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
