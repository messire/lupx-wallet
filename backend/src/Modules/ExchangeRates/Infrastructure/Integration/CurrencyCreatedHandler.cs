using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Reacts to CurrencyCreated — a currency became active (ADR-0004 §7), explicitly or while creating
/// a wallet. It needs a latest rate and its rate history (§9); both come from the background pass,
/// so the subscriber only signals. Runs in the publisher's transaction, hence no HTTP call here
/// (ADR-0001 §4); the signal is delivered after the commit (ExchangeRateRefreshSignal).
/// </summary>
public sealed class CurrencyCreatedHandler(IExchangeRateRefreshSignal refreshSignal)
    : INotificationHandler<DomainEventNotification<CurrencyCreated>>
{
    public Task Handle(DomainEventNotification<CurrencyCreated> notification, CancellationToken cancellationToken)
    {
        refreshSignal.RequestRefresh();
        return Task.CompletedTask;
    }
}
