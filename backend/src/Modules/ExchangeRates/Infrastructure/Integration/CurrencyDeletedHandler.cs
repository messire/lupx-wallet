using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Reacts to CurrencyDeleted (ADR-0002 §4): rates are not "usage" of a currency, so they are
/// removed together with it, latest and historical, where the currency is From or To. Runs in
/// the transaction of DeleteCurrencyCommand (dispatch after SaveChanges, inside the same
/// TransactionScope). Bulk deletes bypass the ChangeTracker and domain events — rates raise
/// none, and the currency deletion itself is already audited.
/// </summary>
public sealed class CurrencyDeletedHandler(
    ILatestExchangeRateRepository latestRepository,
    IHistoricalExchangeRateRepository historicalRepository)
    : INotificationHandler<DomainEventNotification<CurrencyDeleted>>
{
    public async Task Handle(DomainEventNotification<CurrencyDeleted> notification, CancellationToken cancellationToken)
    {
        var currencyId = notification.DomainEvent.CurrencyId;

        await latestRepository.RemoveAllForCurrencyAsync(currencyId, cancellationToken);
        await historicalRepository.RemoveAllForCurrencyAsync(currencyId, cancellationToken);
    }
}
