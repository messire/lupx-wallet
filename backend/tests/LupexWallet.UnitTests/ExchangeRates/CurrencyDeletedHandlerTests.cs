using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// CurrencyDeletedHandler (ADR-0002, §4; R14): rates are not "usage" of a currency, so deleting a
/// currency removes its latest and historical rates (From or To) through the repositories.
/// </summary>
public sealed class CurrencyDeletedHandlerTests
{
    private readonly ILatestExchangeRateRepository _latestRepository = Substitute.For<ILatestExchangeRateRepository>();
    private readonly IHistoricalExchangeRateRepository _historicalRepository = Substitute.For<IHistoricalExchangeRateRepository>();

    [Fact]
    public async Task Handle_CurrencyDeleted_RemovesLatestAndHistoricalRatesForThatCurrencyOnly()
    {
        var currencyId = new CurrencyId(Guid.NewGuid());
        var handler = new CurrencyDeletedHandler(_latestRepository, _historicalRepository);

        await handler.Handle(
            new DomainEventNotification<CurrencyDeleted>(new CurrencyDeleted(currencyId)), CancellationToken.None);

        await _latestRepository.Received(1).RemoveAllForCurrencyAsync(currencyId, Arg.Any<CancellationToken>());
        await _historicalRepository.Received(1).RemoveAllForCurrencyAsync(currencyId, Arg.Any<CancellationToken>());
        await _latestRepository.DidNotReceive().RemoveAllForCurrencyAsync(
            Arg.Is<CurrencyId>(id => id != currencyId), Arg.Any<CancellationToken>());
        await _historicalRepository.DidNotReceive().RemoveAllForCurrencyAsync(
            Arg.Is<CurrencyId>(id => id != currencyId), Arg.Any<CancellationToken>());
    }
}
