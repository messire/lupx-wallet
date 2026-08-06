using FluentAssertions;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Reporting.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Reporting;

/// <summary>
/// GetTotalAmountQueryHandler на моках портов (без БД/HTTP) — риск №6 (docs/PROGRESS.md,
/// П.5.1): кошелек без курса исключается из суммы с явной причиной, это основной путь,
/// не 409 и не курс 1:1.
/// </summary>
public sealed class GetTotalAmountQueryHandlerTests
{
    private static readonly CurrencyId Primary = new(Guid.NewGuid());
    private static readonly CurrencyId Secondary = new(Guid.NewGuid());
    private static readonly WalletId PrimaryWalletId = WalletId.New();
    private static readonly WalletId SecondaryWalletId = WalletId.New();

    private readonly IWalletTotalsSource _walletTotalsSource = Substitute.For<IWalletTotalsSource>();
    private readonly IWalletBalanceOnDateLookup _balanceOnDateLookup = Substitute.For<IWalletBalanceOnDateLookup>();
    private readonly IExchangeRateLookup _exchangeRateLookup = Substitute.For<IExchangeRateLookup>();

    private GetTotalAmountQueryHandler CreateHandler() => new(_walletTotalsSource, _balanceOnDateLookup, _exchangeRateLookup);

    private void SetupWallets(decimal primaryBalance, decimal secondaryBalance, bool secondaryIncludedInTotal = true) =>
        _walletTotalsSource.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<WalletTotalInfo>
        {
            new(PrimaryWalletId, Primary, primaryBalance, IncludeInTotal: true, IsPrimary: true),
            new(SecondaryWalletId, Secondary, secondaryBalance, secondaryIncludedInTotal, IsPrimary: false),
        });

    [Fact]
    public async Task Handle_NoPrimaryWallet_ReturnsNull()
    {
        _walletTotalsSource.GetAllAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<WalletTotalInfo>)[]);

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(Date: null), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WalletInPrimaryCurrency_DoesNotCallExchangeRateLookup()
    {
        SetupWallets(primaryBalance: 100m, secondaryBalance: 0m, secondaryIncludedInTotal: false);

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(Date: null), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Amount.Should().Be(100m);
        result.CurrencyId.Should().Be(Primary.Value);
        await _exchangeRateLookup.DidNotReceiveWithAnyArgs().GetRateAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_SecondaryCurrencyWithRate_ConvertsAndSums()
    {
        SetupWallets(primaryBalance: 100m, secondaryBalance: 30m);
        _exchangeRateLookup.GetRateAsync(Secondary, Primary, Arg.Any<CancellationToken>()).Returns(0.9m);

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(Date: null), CancellationToken.None);

        result!.Amount.Should().Be(100m + 30m * 0.9m);
        result.ExcludedWallets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SecondaryCurrencyWithoutRate_ExcludesWalletWithReasonAndSumsOnlyPrimary()
    {
        SetupWallets(primaryBalance: 100m, secondaryBalance: 30m);
        _exchangeRateLookup.GetRateAsync(Secondary, Primary, Arg.Any<CancellationToken>()).Returns((decimal?)null);

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(Date: null), CancellationToken.None);

        result!.Amount.Should().Be(100m, "кошелек без курса исключается из суммы (решение по риску №6), а не блокирует ответ 409 и не считается по курсу 1:1");
        result.ExcludedWallets.Should().ContainSingle(w => w.WalletId == SecondaryWalletId.Value && !string.IsNullOrWhiteSpace(w.Reason));
    }

    [Fact]
    public async Task Handle_NotIncludedInTotal_IsSkippedEntirelyAndNeverExcluded()
    {
        SetupWallets(primaryBalance: 100m, secondaryBalance: 30m, secondaryIncludedInTotal: false);
        _exchangeRateLookup.GetRateAsync(Secondary, Primary, Arg.Any<CancellationToken>()).Returns((decimal?)null);

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(Date: null), CancellationToken.None);

        result!.Amount.Should().Be(100m);
        result.ExcludedWallets.Should().BeEmpty("кошелек не включен в общую сумму — он не «исключен из-за отсутствия курса», а изначально не участвует");
    }

    [Fact]
    public async Task Handle_HistoricalDate_UsesBalanceOnDateLookupAndHistoricalRate()
    {
        var date = new DateOnly(2026, 1, 1);
        SetupWallets(primaryBalance: 0m, secondaryBalance: 0m);
        _balanceOnDateLookup.GetBalanceOnDateAsync(PrimaryWalletId, date, Arg.Any<CancellationToken>()).Returns(50m);
        _balanceOnDateLookup.GetBalanceOnDateAsync(SecondaryWalletId, date, Arg.Any<CancellationToken>()).Returns(20m);
        _exchangeRateLookup.GetOrFetchHistoricalRateAsync(Secondary, Primary, date, Arg.Any<CancellationToken>())
            .Returns(((decimal Rate, DateOnly RatesAsOfDate)?)(0.8m, date));

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(date), CancellationToken.None);

        result!.Amount.Should().Be(50m + 20m * 0.8m);
        result.Date.Should().Be(date);
        result.RatesAsOfDate.Should().Be(date);
    }

    [Fact]
    public async Task Handle_DateBeforeAccountingStart_BalanceOnDateLookupReturnsZero_TotalIsZero()
    {
        var date = new DateOnly(2020, 1, 1);
        SetupWallets(primaryBalance: 0m, secondaryBalance: 0m);
        _balanceOnDateLookup.GetBalanceOnDateAsync(PrimaryWalletId, date, Arg.Any<CancellationToken>()).Returns(0m);
        _balanceOnDateLookup.GetBalanceOnDateAsync(SecondaryWalletId, date, Arg.Any<CancellationToken>()).Returns(0m);
        _exchangeRateLookup.GetOrFetchHistoricalRateAsync(Secondary, Primary, date, Arg.Any<CancellationToken>())
            .Returns(((decimal Rate, DateOnly RatesAsOfDate)?)(1m, date));

        var result = await CreateHandler().Handle(new GetTotalAmountQuery(date), CancellationToken.None);

        result!.Amount.Should().Be(0m);
        result.ExcludedWallets.Should().BeEmpty();
    }
}
