using FluentAssertions;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Wallets;

/// <summary>
/// Покрытие агрегата Wallet (ddd-model.md, §2.1, §5): создание, PrimaryWalletPolicy-протокол
/// (MarkAsPrimary/UnmarkAsPrimary), Archive, ChangeCurrency, EnsureCanBeDeleted, ApplyBalanceDelta.
/// </summary>
public sealed class WalletTests
{
    private static Money Usd(decimal amount) => new(amount, new CurrencyId(Guid.NewGuid()));

    private static Wallet CreateWallet(Money? initialBalance = null, bool isFirstWallet = false, bool includeInTotal = false) =>
        Wallet.Create(
            id: new WalletId(Guid.NewGuid()),
            name: "Кошелек",
            walletTypeId: new WalletTypeId(Guid.NewGuid()),
            initialBalance: initialBalance ?? Usd(100m),
            accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            purposeDescription: null,
            includeInTotal: includeInTotal,
            displayOrder: 0,
            color: null,
            icon: null,
            isFirstWallet: isFirstWallet);

    [Fact]
    public void Create_FirstWallet_IsMarkedPrimaryAndIncludedInTotal()
    {
        var initialBalance = Usd(100m);

        var wallet = Wallet.Create(
            id: new WalletId(Guid.NewGuid()),
            name: "Наличные",
            walletTypeId: new WalletTypeId(Guid.NewGuid()),
            initialBalance: initialBalance,
            accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            purposeDescription: null,
            includeInTotal: false,
            displayOrder: 0,
            color: null,
            icon: null,
            isFirstWallet: true);

        wallet.IsPrimary.Should().BeTrue();
        wallet.IncludeInTotal.Should().BeTrue("основной кошелек всегда включен в общую сумму (Q8)");
        wallet.CurrentBalance.Should().Be(initialBalance);
    }

    [Fact]
    public void Create_NotFirstWallet_IsNotPrimaryAndRespectsRequestedIncludeInTotal()
    {
        var wallet = CreateWallet(isFirstWallet: false, includeInTotal: false);

        wallet.IsPrimary.Should().BeFalse();
        wallet.IncludeInTotal.Should().BeFalse();
    }

    [Fact]
    public void Create_WithBlankName_ThrowsWalletNameRequiredException()
    {
        var act = () => Wallet.Create(
            id: new WalletId(Guid.NewGuid()),
            name: "   ",
            walletTypeId: new WalletTypeId(Guid.NewGuid()),
            initialBalance: Usd(0m),
            accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            purposeDescription: null,
            includeInTotal: false,
            displayOrder: 0,
            color: null,
            icon: null,
            isFirstWallet: false);

        act.Should().Throw<WalletNameRequiredException>();
    }

    [Fact]
    public void ApplyBalanceDelta_WithMismatchedCurrency_ThrowsWalletCurrencyMismatchException()
    {
        var wallet = CreateWallet(Usd(50m), includeInTotal: true);

        var act = () => wallet.ApplyBalanceDelta(Usd(10m));

        act.Should().Throw<WalletCurrencyMismatchException>();
    }

    [Fact]
    public void ApplyBalanceDelta_WithMatchingCurrency_UpdatesCurrentBalanceAndKeepsInitialBalanceUnchanged()
    {
        var wallet = CreateWallet(Usd(50m));
        var delta = new Money(20m, wallet.CurrencyId);

        wallet.ApplyBalanceDelta(delta);

        wallet.CurrentBalance.Amount.Should().Be(70m);
        wallet.InitialBalance.Amount.Should().Be(50m, "InitialBalance не меняется дельтами баланса");
    }

    [Fact]
    public void Archive_PrimaryWallet_ThrowsCannotArchivePrimaryWalletException()
    {
        var wallet = CreateWallet(isFirstWallet: true);

        var act = wallet.Archive;

        act.Should().Throw<CannotArchivePrimaryWalletException>();
    }

    [Fact]
    public void Archive_NonPrimaryWallet_SetsIsArchivedTrue()
    {
        var wallet = CreateWallet(isFirstWallet: false);

        wallet.Archive();

        wallet.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void MarkAsPrimary_ArchivedWallet_ThrowsCannotSetArchivedWalletAsPrimaryException()
    {
        var wallet = CreateWallet(isFirstWallet: false);
        wallet.Archive();

        var act = () => wallet.MarkAsPrimary(previousPrimaryWalletId: null);

        act.Should().Throw<CannotSetArchivedWalletAsPrimaryException>();
    }

    [Fact]
    public void MarkAsPrimary_ActiveWallet_SetsIsPrimaryAndForcesIncludeInTotal()
    {
        var wallet = CreateWallet(isFirstWallet: false, includeInTotal: false);

        wallet.MarkAsPrimary(previousPrimaryWalletId: new WalletId(Guid.NewGuid()));

        wallet.IsPrimary.Should().BeTrue();
        wallet.IncludeInTotal.Should().BeTrue("Q8: у основного кошелька IncludeInTotal нельзя выключить");
    }

    [Fact]
    public void UnmarkAsPrimary_ClearsIsPrimaryFlag()
    {
        var wallet = CreateWallet(isFirstWallet: true);

        wallet.UnmarkAsPrimary();

        wallet.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void ChangeCurrency_WithHistory_ThrowsWalletCurrencyChangeNotAllowedException()
    {
        var wallet = CreateWallet();

        var act = () => wallet.ChangeCurrency(new CurrencyId(Guid.NewGuid()), hasHistory: true);

        act.Should().Throw<WalletCurrencyChangeNotAllowedException>();
    }

    [Fact]
    public void ChangeCurrency_WithoutHistory_ChangesCurrencyId()
    {
        var wallet = CreateWallet();
        var newCurrencyId = new CurrencyId(Guid.NewGuid());

        wallet.ChangeCurrency(newCurrencyId, hasHistory: false);

        wallet.CurrencyId.Should().Be(newCurrencyId);
    }

    [Fact]
    public void EnsureCanBeDeleted_WithHistory_ThrowsWalletDeletionNotAllowedException()
    {
        var wallet = CreateWallet();

        var act = () => wallet.EnsureCanBeDeleted(hasHistory: true);

        act.Should().Throw<WalletDeletionNotAllowedException>();
    }

    [Fact]
    public void EnsureCanBeDeleted_WithoutHistory_DoesNotThrow()
    {
        var wallet = CreateWallet();

        var act = () => wallet.EnsureCanBeDeleted(hasHistory: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void UpdateDetails_OnPrimaryWallet_ForcesIncludeInTotalTrueEvenWhenRequestedFalse()
    {
        var wallet = CreateWallet(isFirstWallet: true);

        wallet.UpdateDetails(
            name: "Переименован",
            walletTypeId: wallet.WalletTypeId,
            purposeDescription: null,
            includeInTotal: false,
            displayOrder: 1,
            color: null,
            icon: null);

        wallet.IncludeInTotal.Should().BeTrue("Q8: у основного кошелька нельзя выключить IncludeInTotal даже через UpdateDetails");
        wallet.Name.Should().Be("Переименован");
    }

    [Fact]
    public void UpdateDetails_WithBlankName_ThrowsWalletNameRequiredException()
    {
        var wallet = CreateWallet();

        var act = () => wallet.UpdateDetails(
            name: " ",
            walletTypeId: wallet.WalletTypeId,
            purposeDescription: null,
            includeInTotal: false,
            displayOrder: 0,
            color: null,
            icon: null);

        act.Should().Throw<WalletNameRequiredException>();
    }
}
