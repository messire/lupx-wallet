namespace LupexWallet.SharedKernel;

/// <summary>
/// Маркер доменного события (ddd-model.md, §6). Не зависит от MediatR — Domain-слой
/// не должен знать о механизме доставки; обертку под MediatR.INotification строит
/// BuildingBlocks.Infrastructure (см. DomainEventNotification&lt;T&gt;).
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
