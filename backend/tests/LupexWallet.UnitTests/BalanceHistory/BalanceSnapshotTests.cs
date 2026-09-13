using FluentAssertions;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.BalanceHistory;

/// <summary>BalanceSnapshot (ddd-model.md, §2.6): идентифицируется (WalletId, SnapshotDate), UpdateBalance защищает от смены валюты.</summary>
public sealed class BalanceSnapshotTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());

    [Fact]
    public void Create_SetsIdentityAndBalance()
    {
        var walletId = WalletId.New();
        var date = new DateOnly(2026, 1, 1);
        var balance = new Money(500m, Currency);

        var snapshot = BalanceSnapshot.Create(walletId, date, balance);

        snapshot.WalletId.Should().Be(walletId);
        snapshot.SnapshotDate.Should().Be(date);
        snapshot.Balance.Should().Be(balance);
    }

    [Fact]
    public void Create_RaisesBalanceSnapshotCreatedEvent()
    {
        var snapshot = BalanceSnapshot.Create(WalletId.New(), new DateOnly(2026, 1, 1), new Money(100m, Currency));

        snapshot.DomainEvents.OfType<BalanceSnapshotCreated>().Should().ContainSingle();
    }

    [Fact]
    public void UpdateBalance_DifferentCurrency_ThrowsInvalidOperationException()
    {
        var snapshot = BalanceSnapshot.Create(WalletId.New(), new DateOnly(2026, 1, 1), new Money(100m, Currency));
        var otherCurrencyBalance = new Money(100m, new CurrencyId(Guid.NewGuid()));

        var act = () => snapshot.UpdateBalance(otherCurrencyBalance);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateBalance_SameCurrency_UpdatesAmountAndRaisesBalanceSnapshotUpdated()
    {
        var snapshot = BalanceSnapshot.Create(WalletId.New(), new DateOnly(2026, 1, 1), new Money(100m, Currency));

        snapshot.UpdateBalance(new Money(180m, Currency));

        snapshot.Balance.Amount.Should().Be(180m);
        var updatedEvent = snapshot.DomainEvents.OfType<BalanceSnapshotUpdated>().Single();
        updatedEvent.OldBalance.Amount.Should().Be(100m);
        updatedEvent.NewBalance.Amount.Should().Be(180m);
    }
}
