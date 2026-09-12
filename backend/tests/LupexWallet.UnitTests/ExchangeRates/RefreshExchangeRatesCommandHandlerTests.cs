using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// Политика fallback при недоступности Frankfurter (ADR-0001 п.6) и пропуск валют, не
/// поддерживаемых Frankfurter (ADR-0001, "Последствия") — на моках портов, без БД/HTTP
/// (docs/PROGRESS.md, W1.1 DoD пп.3/6).
/// </summary>
public sealed class RefreshExchangeRatesCommandHandlerTests
{
    private static readonly CurrencyId Primary = new(Guid.NewGuid());
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Btc = new(Guid.NewGuid());

    private readonly ILatestExchangeRateRepository _repository = Substitute.For<ILatestExchangeRateRepository>();
    private readonly IExchangeRatesUnitOfWork _unitOfWork = Substitute.For<IExchangeRatesUnitOfWork>();
    private readonly IFrankfurterClient _frankfurterClient = Substitute.For<IFrankfurterClient>();
    private readonly IWalletCurrencySet _walletCurrencySet = Substitute.For<IWalletCurrencySet>();
    private readonly ICurrencyLookup _currencyLookup = Substitute.For<ICurrencyLookup>();
    private readonly IExchangeRateRefreshSignal _refreshSignal = Substitute.For<IExchangeRateRefreshSignal>();
    private readonly IDomainEventDispatcher _eventDispatcher = Substitute.For<IDomainEventDispatcher>();

    private RefreshExchangeRatesCommandHandler CreateHandler() => new(
        _repository, _unitOfWork, _frankfurterClient, _walletCurrencySet, _currencyLookup, _refreshSignal,
        _eventDispatcher, Substitute.For<ILogger<RefreshExchangeRatesCommandHandler>>());

    private void SetupCurrencies()
    {
        _currencyLookup.GetAsync(Primary, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Primary, "EUR", true));
        _currencyLookup.GetAsync(Usd, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Usd, "USD", true));
        _currencyLookup.GetAsync(Btc, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Btc, "BTC", true));
    }

    [Fact]
    public async Task Handle_NoPrimaryWallet_SkipsRefreshEntirely()
    {
        _walletCurrencySet.GetAsync(Arg.Any<CancellationToken>()).Returns(new WalletCurrencySetResult(null, []));
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ExchangeRateQuote>)[]);

        var result = await CreateHandler().Handle(new RefreshExchangeRatesCommand(IsManualTrigger: false), CancellationToken.None);

        result.HadFailures.Should().BeFalse();
        result.LastSuccessfulUpdate.Should().BeNull();
        await _frankfurterClient.DidNotReceiveWithAnyArgs().GetLatestRateAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_FrankfurterUnavailable_KeepsOldRateAndReportsFailure()
    {
        SetupCurrencies();
        _walletCurrencySet.GetAsync(Arg.Any<CancellationToken>()).Returns(new WalletCurrencySetResult(Primary, [Primary, Usd]));

        var existingQuote = ExchangeRateQuote.Create(Usd, Primary, new ExchangeRateValue(0.9m), DateTimeOffset.UtcNow.AddDays(-1));
        _repository.FindAsync(Usd, Primary, Arg.Any<CancellationToken>()).Returns(existingQuote);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ExchangeRateQuote>)[existingQuote]);

        _frankfurterClient.GetLatestRateAsync("USD", "EUR", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<decimal>(new FrankfurterUnavailableException("недоступен")));

        var result = await CreateHandler().Handle(new RefreshExchangeRatesCommand(IsManualTrigger: true), CancellationToken.None);

        result.HadFailures.Should().BeTrue();
        existingQuote.Rate.Rate.Should().Be(0.9m, "курс при ошибке источника не должен меняться (ADR-0001 п.6)");
        await _eventDispatcher.Received(1).DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.OfType<ExchangeRateUpdateFailed>().Any()),
            Arg.Any<CancellationToken>());
        _refreshSignal.Received(1).NotifyManualRefreshCompleted();
    }

    [Fact]
    public async Task Handle_UnsupportedCurrency_SkipsPairWithoutReportingFailure()
    {
        SetupCurrencies();
        _walletCurrencySet.GetAsync(Arg.Any<CancellationToken>()).Returns(new WalletCurrencySetResult(Primary, [Primary, Btc]));
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ExchangeRateQuote>)[]);

        _frankfurterClient.GetLatestRateAsync("BTC", "EUR", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<decimal>(new FrankfurterUnsupportedCurrencyException("BTC", "EUR")));

        var result = await CreateHandler().Handle(new RefreshExchangeRatesCommand(IsManualTrigger: false), CancellationToken.None);

        result.HadFailures.Should().BeFalse("валюта, не поддерживаемая Frankfurter, пропускается молча, а не считается отказом источника");
        await _eventDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task Handle_SuccessfulFetch_PersistsNewQuoteAndRaisesExchangeRatesUpdated()
    {
        SetupCurrencies();
        _walletCurrencySet.GetAsync(Arg.Any<CancellationToken>()).Returns(new WalletCurrencySetResult(Primary, [Primary, Usd]));
        _repository.FindAsync(Usd, Primary, Arg.Any<CancellationToken>()).Returns((ExchangeRateQuote?)null);
        _repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
            callInfo => (IReadOnlyList<ExchangeRateQuote>)[ExchangeRateQuote.Create(Usd, Primary, new ExchangeRateValue(0.91m), DateTimeOffset.UtcNow)]);
        _frankfurterClient.GetLatestRateAsync("USD", "EUR", Arg.Any<CancellationToken>()).Returns(0.91m);

        var result = await CreateHandler().Handle(new RefreshExchangeRatesCommand(IsManualTrigger: false), CancellationToken.None);

        result.HadFailures.Should().BeFalse();
        _repository.Received(1).Add(Arg.Is<ExchangeRateQuote>(q => q.FromCurrencyId == Usd && q.ToCurrencyId == Primary));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _eventDispatcher.Received(1).DispatchAsync(
            Arg.Is<IEnumerable<IDomainEvent>>(events => events.OfType<ExchangeRatesUpdated>().Any()),
            Arg.Any<CancellationToken>());
        _refreshSignal.DidNotReceiveWithAnyArgs().NotifyManualRefreshCompleted();
    }
}
