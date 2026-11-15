using FluentAssertions;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>ADR-0010 §4, B7: free funds = balance − reserves of all active funds on the wallet; expenses are not blocked.</summary>
public sealed class ReservationPolicyTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly WalletId Wallet = new(Guid.NewGuid());

    private static Fund CreateFund() => Fund.Create(FundId.New(), "Фонд", 1000m, Usd, null, null);

    private static ReservationPolicy Policy(decimal balance, decimal reservedOnWallet, bool archived = false, CurrencyId? currency = null, bool walletExists = true)
    {
        var repository = Substitute.For<IFundRepository>();
        repository.GetReservedOnWalletAsync(Wallet, Arg.Any<CancellationToken>()).Returns(reservedOnWallet);

        var gateway = Substitute.For<IWalletBalanceGateway>();
        var walletCurrency = currency ?? Usd;
        gateway.GetAsync(Wallet, Arg.Any<CancellationToken>()).Returns(walletExists
            ? new WalletBalanceInfo(Wallet, walletCurrency, new Money(0m, walletCurrency), new Money(balance, walletCurrency), new DateOnly(2026, 1, 1), archived)
            : null);

        return new ReservationPolicy(repository, gateway);
    }

    [Fact]
    public async Task Reserve_WithinFreeFunds_CreatesAllocation()
    {
        var fund = CreateFund();

        await Policy(balance: 500m, reservedOnWallet: 200m).ReserveAsync(fund, Wallet, 300m, CancellationToken.None);

        fund.AllocatedOn(Wallet).Should().Be(300m, "exactly the free amount can be reserved");
    }

    [Fact]
    public async Task Reserve_MoreThanFreeFunds_Throws()
    {
        var fund = CreateFund();

        var act = () => Policy(balance: 500m, reservedOnWallet: 200m).ReserveAsync(fund, Wallet, 300.0001m, CancellationToken.None);

        (await act.Should().ThrowAsync<FundInsufficientFreeFundsException>()).Which.Free.Should().Be(300m);
        fund.AllocatedOn(Wallet).Should().Be(0m);
    }

    [Fact]
    public async Task Reserve_BalanceBelowReserves_ReportsZeroFree()
    {
        var fund = CreateFund();

        var act = () => Policy(balance: 100m, reservedOnWallet: 150m).ReserveAsync(fund, Wallet, 1m, CancellationToken.None);

        (await act.Should().ThrowAsync<FundInsufficientFreeFundsException>()).Which.Free.Should().Be(0m);
    }

    [Fact]
    public async Task Reserve_ArchivedWallet_Throws()
    {
        var act = () => Policy(balance: 500m, reservedOnWallet: 0m, archived: true).ReserveAsync(CreateFund(), Wallet, 1m, CancellationToken.None);

        await act.Should().ThrowAsync<FundWalletArchivedException>();
    }

    [Fact]
    public async Task Reserve_WalletInAnotherCurrency_Throws()
    {
        var act = () => Policy(balance: 500m, reservedOnWallet: 0m, currency: new CurrencyId(Guid.NewGuid()))
            .ReserveAsync(CreateFund(), Wallet, 1m, CancellationToken.None);

        await act.Should().ThrowAsync<FundWalletCurrencyMismatchException>();
    }

    [Fact]
    public async Task Reserve_UnknownWallet_Throws()
    {
        var act = () => Policy(balance: 0m, reservedOnWallet: 0m, walletExists: false).ReserveAsync(CreateFund(), Wallet, 1m, CancellationToken.None);

        await act.Should().ThrowAsync<FundWalletNotFoundException>();
    }

    [Fact]
    public async Task Reserve_NonPositiveAmount_IsAValidationErrorEvenWithoutFreeFunds()
    {
        var act = () => Policy(balance: -10m, reservedOnWallet: 0m).ReserveAsync(CreateFund(), Wallet, -1m, CancellationToken.None);

        await act.Should().ThrowAsync<FundAmountMustBePositiveException>();
    }
}
