using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.BuildingBlocks.Infrastructure;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}

/// <summary>
/// Публикует доменные события через MediatR.IPublisher, оборачивая каждое в
/// DomainEventNotification&lt;T&gt;. Вызывается изнутри TransactionBehavior — то есть
/// подписчики (Wallets/BalanceHistory/Audit и т.д.) отрабатывают в той же транзакции,
/// что и исходная команда (high-level-architecture.md, §4).
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
