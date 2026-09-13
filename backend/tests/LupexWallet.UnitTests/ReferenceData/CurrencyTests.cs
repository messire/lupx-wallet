using FluentAssertions;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>Currency (ddd-model.md, §2.2; решено при проектировании — расширяемый справочник, не жестко ISO 4217).</summary>
public sealed class CurrencyTests
{
    [Fact]
    public void Create_WithBlankCode_ThrowsCurrencyCodeRequiredException()
    {
        var act = () => Currency.Create(CurrencyId.New(), "  ", "Доллар США");

        act.Should().Throw<CurrencyCodeRequiredException>();
    }

    [Fact]
    public void Create_WithBlankName_ThrowsReferenceItemNameRequiredException()
    {
        var act = () => Currency.Create(CurrencyId.New(), "USD", "  ");

        act.Should().Throw<ReferenceItemNameRequiredException>();
    }

    [Fact]
    public void Create_NormalizesCodeToTrimmedUppercase()
    {
        var currency = Currency.Create(CurrencyId.New(), " usd ", "Доллар США");

        currency.Code.Should().Be("USD");
        currency.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var currency = Currency.Create(CurrencyId.New(), "EUR", "Евро");

        currency.Deactivate();

        currency.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EnsureCanBeDeleted_WhenUsed_ThrowsReferenceItemInUseException()
    {
        var currency = Currency.Create(CurrencyId.New(), "EUR", "Евро");

        var act = () => currency.EnsureCanBeDeleted(isUsed: true);

        act.Should().Throw<ReferenceItemInUseException>();
    }
}
