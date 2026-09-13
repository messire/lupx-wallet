using FluentAssertions;
using LupexWallet.ExchangeRates.Domain;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>Инвариант ddd-model.md §5 "ExchangeRateQuote": Rate &gt; 0.</summary>
public sealed class ExchangeRateValueTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.0001)]
    public void Constructor_NonPositiveRate_Throws(decimal rate)
    {
        var act = () => new ExchangeRateValue(rate);

        act.Should().Throw<InvalidExchangeRateException>();
    }

    [Theory]
    [InlineData(0.0001)]
    [InlineData(1)]
    [InlineData(123456.789)]
    public void Constructor_PositiveRate_Succeeds(decimal rate)
    {
        var value = new ExchangeRateValue(rate);

        value.Rate.Should().Be(rate);
    }
}
