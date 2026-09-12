using FluentAssertions;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Wallets;

/// <summary>
/// PrimaryWalletPolicy (ddd-model.md, §2.1) — инвариант «ровно один основной кошелек» на
/// уровне коллекции агрегатов. Здесь покрывается только определение "первый ли это кошелек";
/// снятие/назначение флага — Wallet.MarkAsPrimary/UnmarkAsPrimary (см. WalletTests).
/// </summary>
public sealed class PrimaryWalletPolicyTests
{
    [Fact]
    public async Task IsFirstWalletAsync_NoExistingWallets_ReturnsTrue()
    {
        var repository = Substitute.For<IWalletRepository>();
        repository.AnyExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());

        var result = await policy.IsFirstWalletAsync(CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsFirstWalletAsync_HasExistingWallets_ReturnsFalse()
    {
        var repository = Substitute.For<IWalletRepository>();
        repository.AnyExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        var policy = new PrimaryWalletPolicy(repository, Substitute.For<IWalletsUnitOfWork>());

        var result = await policy.IsFirstWalletAsync(CancellationToken.None);

        result.Should().BeFalse();
    }
}
