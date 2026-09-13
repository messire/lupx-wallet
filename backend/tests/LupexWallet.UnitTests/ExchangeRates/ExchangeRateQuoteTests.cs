using FluentAssertions;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

public sealed class ExchangeRateQuoteTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Eur = new(Guid.NewGuid());

    [Fact]
    public void Create_SameCurrencyPair_ThrowsSameCurrencyExchangeRateException()
    {
        var act = () => ExchangeRateQuote.Create(Usd, Usd, new ExchangeRateValue(1m), DateTimeOffset.UtcNow);

        act.Should().Throw<SameCurrencyExchangeRateException>();
    }

    [Fact]
    public void Create_DifferentCurrencies_SetsRateAndFetchedAt()
    {
        var fetchedAt = DateTimeOffset.UtcNow;

        var quote = ExchangeRateQuote.Create(Usd, Eur, new ExchangeRateValue(0.92m), fetchedAt);

        quote.FromCurrencyId.Should().Be(Usd);
        quote.ToCurrencyId.Should().Be(Eur);
        quote.Rate.Rate.Should().Be(0.92m);
        quote.FetchedAt.Should().Be(fetchedAt);
    }

    [Fact]
    public void UpdateRate_ChangesRateAndFetchedAt()
    {
        var quote = ExchangeRateQuote.Create(Usd, Eur, new ExchangeRateValue(0.9m), DateTimeOffset.UtcNow.AddDays(-1));
        var newFetchedAt = DateTimeOffset.UtcNow;

        quote.UpdateRate(new ExchangeRateValue(0.95m), newFetchedAt);

        quote.Rate.Rate.Should().Be(0.95m);
        quote.FetchedAt.Should().Be(newFetchedAt);
    }
}
