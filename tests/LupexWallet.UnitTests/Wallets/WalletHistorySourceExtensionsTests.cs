using FluentAssertions;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Wallets;

/// <summary>
/// ADR-0009, случай a — агрегация IEnumerable&lt;IWalletHistorySource&gt; на стороне
/// потребителя (Wallets.Application). Пустой список реализаций — ошибка конфигурации DI,
/// не «истории нет» (см. doc-комментарий WalletHistorySourceExtensions).
/// </summary>
public sealed class WalletHistorySourceExtensionsTests
{
    private static readonly WalletId AnyWalletId = new(Guid.NewGuid());

    [Fact]
    public async Task AnyHasHistoryAsync_NoRegisteredSources_ThrowsInvalidOperationException()
    {
        var sources = Array.Empty<IWalletHistorySource>();

        var act = () => sources.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AnyHasHistoryAsync_AllSourcesReportNoHistory_ReturnsFalse()
    {
        var first = Substitute.For<IWalletHistorySource>();
        first.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(false);
        var second = Substitute.For<IWalletHistorySource>();
        second.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new[] { first, second }.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnyHasHistoryAsync_OneOfSeveralSourcesReportsHistory_ReturnsTrue()
    {
        var operations = Substitute.For<IWalletHistorySource>();
        operations.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(false);
        var balanceHistory = Substitute.For<IWalletHistorySource>();
        balanceHistory.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new[] { operations, balanceHistory }.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        result.Should().BeTrue();
    }
}
