using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.BuildingBlocks.Infrastructure;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes domain events via MediatR.IPublisher, wrapping each in
/// DomainEventNotification&lt;T&gt;. Called from within TransactionBehavior, so subscribers
/// (Wallets/BalanceHistory/Audit, etc.) run in the same transaction as the originating
/// command (high-level-architecture.md, §4).
/// </summary>
public sealed class MediatRDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
            var notification = (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
            await publisher.Publish(notification, cancellationToken);
        }
    }
}
