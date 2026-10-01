using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Reacts to WalletCreated (ADR-0004 §9): the new wallet's accounting start date can precede the
/// earliest one known so far, which moves the left edge of the rate history. Signals only — the
/// background pass computes what is missing; delivery happens after the publisher's transaction commits.
/// </summary>
public sealed class WalletCreatedHandler(IExchangeRateRefreshSignal refreshSignal)
    : INotificationHandler<DomainEventNotification<WalletCreated>>
{
    public Task Handle(DomainEventNotification<WalletCreated> notification, CancellationToken cancellationToken)
    {
        refreshSignal.RequestRefresh();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reacts to WalletCurrencyChanged (ADR-0004 §5, §9): for the primary wallet this changes the primary
/// currency, so rates and history to the new currency must be loaded; for another wallet the
/// pass finds nothing to do. Signals only.
/// </summary>
public sealed class WalletCurrencyChangedHandler(IExchangeRateRefreshSignal refreshSignal)
    : INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>
{
    public Task Handle(DomainEventNotification<WalletCurrencyChanged> notification, CancellationToken cancellationToken)
    {
        refreshSignal.RequestRefresh();
        return Task.CompletedTask;
    }
}
