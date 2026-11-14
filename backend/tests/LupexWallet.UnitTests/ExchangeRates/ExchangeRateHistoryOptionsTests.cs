using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>ExchangeRates:HistoryChunkDays binding and validation (ADR-0004 §9): default 366, a value below 1 is rejected.</summary>
public sealed class ExchangeRateHistoryOptionsTests
{
    private static ExchangeRateHistoryOptions Resolve(string? chunkDays)
    {
        var values = new Dictionary<string, string?> { ["ConnectionStrings:LupexWallet"] = "Host=localhost" };
        if (chunkDays is not null)
        {
            values["ExchangeRates:HistoryChunkDays"] = chunkDays;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddExchangeRatesModule(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ExchangeRateHistoryOptions>>().Value;
    }

    [Fact]
    public void NotConfigured_UsesTheDefault() => Resolve(null).HistoryChunkDays.Should().Be(366);

    [Fact]
    public void Configured_IsBound() => Resolve("100").HistoryChunkDays.Should().Be(100);

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void BelowOne_IsRejected(string value)
    {
        var act = () => Resolve(value);

        act.Should().Throw<OptionsValidationException>().WithMessage("*HistoryChunkDays*");
    }
}
