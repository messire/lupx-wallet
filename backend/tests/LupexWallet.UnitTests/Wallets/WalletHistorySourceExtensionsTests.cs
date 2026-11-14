using FluentAssertions;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Wallets;

/// <summary>
/// ADR-0001, §2, способ d, ADR-0002, §2 — агрегация IEnumerable&lt;IWalletHistorySource&gt; на стороне
/// потребителя (Wallets.Application); единственный владелец данных — Operations. Пустой список реализаций — ошибка конфигурации DI,
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
    public async Task AnyHasHistoryAsync_SingleSourceReportsNoHistory_ReturnsFalse()
    {
        var operations = Substitute.For<IWalletHistorySource>();
        operations.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new[] { operations }.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnyHasHistoryAsync_SingleSourceReportsHistory_ReturnsTrue()
    {
        var operations = Substitute.For<IWalletHistorySource>();
        operations.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new[] { operations }.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AnyHasHistoryAsync_OneOfSeveralSourcesReportsHistory_ReturnsTrue()
    {
        // Агрегация по-прежнему поддерживает несколько источников (контракт порта), хотя
        // по ADR-0002 §2 в композиции Host зарегистрирован один.
        var first = Substitute.For<IWalletHistorySource>();
        first.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(false);
        var second = Substitute.For<IWalletHistorySource>();
        second.HasHistoryAsync(AnyWalletId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new[] { first, second }.AnyHasHistoryAsync(AnyWalletId, CancellationToken.None);

        result.Should().BeTrue();
    }
}
