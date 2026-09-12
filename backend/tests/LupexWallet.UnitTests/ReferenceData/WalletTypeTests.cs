using FluentAssertions;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>WalletType (ddd-model.md, §2.2, §5): создание, деактивация, защита удаления используемого элемента.</summary>
public sealed class WalletTypeTests
{
    [Fact]
    public void Create_WithBlankName_ThrowsReferenceItemNameRequiredException()
    {
        var act = () => WalletType.Create(WalletTypeId.New(), "  ");

        act.Should().Throw<ReferenceItemNameRequiredException>();
    }

    [Fact]
    public void Create_TrimsNameAndIsActiveByDefault()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "  Наличные  ");

        walletType.Name.Should().Be("Наличные");
        walletType.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Карта");

        walletType.Deactivate();

        walletType.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EnsureCanBeDeleted_WhenUsed_ThrowsReferenceItemInUseException()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Карта");

        var act = () => walletType.EnsureCanBeDeleted(isUsed: true);

        act.Should().Throw<ReferenceItemInUseException>();
    }

    [Fact]
    public void EnsureCanBeDeleted_WhenNotUsed_DoesNotThrow()
    {
        var walletType = WalletType.Create(WalletTypeId.New(), "Карта");

        var act = () => walletType.EnsureCanBeDeleted(isUsed: false);

        act.Should().NotThrow();
    }
}
