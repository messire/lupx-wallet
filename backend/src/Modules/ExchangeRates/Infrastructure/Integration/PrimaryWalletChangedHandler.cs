using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Reacts to PrimaryWalletChanged (ADR-0004 p.5, p.9) — rates and history must be re-fetched against
/// the new primary currency. Like the event's other subscribers (BalanceHistory etc.), runs in the
/// same transaction as the command that raised the event (SetPrimaryWallet) — so a synchronous HTTP
/// call is forbidden here (ADR-0001 §4): the handler only signals the background service
/// (RequestRefresh), which performs the pass in its own DI scope after the command's transaction commits.
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
