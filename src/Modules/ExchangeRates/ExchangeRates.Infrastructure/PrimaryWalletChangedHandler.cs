using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Реакция на PrimaryWalletChanged (ADR-0001 п.5) — курсы должны перезапрашиваться с новой
/// основной валютой. Как и остальные подписчики этого события (BalanceHistory и т.д.),
/// выполняется в той же транзакции, что и команда, поднявшая событие (SetPrimaryWallet,
/// W1.2) — поэтому здесь запрещено делать HTTP-запрос синхронно (ADR-0011): обработчик
/// только сигналит фоновому сервису (RequestRefresh), который выполнит реальное обновление
/// курсов асинхронно, в собственном DI-scope, вне транзакции SetPrimaryWallet.
/// </summary>
public sealed class PrimaryWalletChangedHandler(IExchangeRateRefreshSignal refreshSignal)
    : INotificationHandler<DomainEventNotification<PrimaryWalletChanged>>
{
    public Task Handle(DomainEventNotification<PrimaryWalletChanged> notification, CancellationToken cancellationToken)
    {
        refreshSignal.RequestRefresh();
        return Task.CompletedTask;
    }
}
