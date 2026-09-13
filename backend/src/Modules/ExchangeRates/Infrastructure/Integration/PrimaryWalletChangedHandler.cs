using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Reacts to PrimaryWalletChanged (ADR-0001 p.5) — rates must be re-fetched against the new
/// primary currency. Like the event's other subscribers (BalanceHistory etc.), runs in the
/// same transaction as the command that raised the event (SetPrimaryWallet, W1.2) — so a
/// synchronous HTTP call is forbidden here (ADR-0011): the handler only signals the
/// background service (RequestRefresh), which performs the actual rate refresh
/// asynchronously, in its own DI scope, outside SetPrimaryWallet's transaction.
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
