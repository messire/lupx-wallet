using System.Net;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// Registration of the named HttpClient "Frankfurter" (ExchangeRatesModuleExtensions).
/// Regression for the production incident of 2026-11-16: the primary handler was a shared
/// Singleton, so once IHttpClientFactory rotated the handler chain (HandlerLifetime, 2 min by
/// default) and disposed the expired one, every later request — POST /exchange-rates/refresh
/// included — failed with ObjectDisposedException (500 with an empty body).
/// </summary>
public sealed class FrankfurterHttpClientRegistrationTests
{
    [Fact]
    public async Task Disposing_an_expired_handler_chain_does_not_break_the_next_chain()
    {
        await using var provider = BuildProvider();

        // Exactly what IHttpClientFactory does on rotation: build a fresh chain from the
        // registered configuration and dispose the expired one.
        var expired = new HttpMessageInvoker(BuildChain(provider), disposeHandler: true);
        using var active = new HttpMessageInvoker(BuildChain(provider), disposeHandler: true);
        expired.Dispose();

        // No test override: the request must reach the real socket handler. Nothing listens on
        // port 1, so the expected outcome is a connection error — never ObjectDisposedException.
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/v2/currencies");
        var send = () => active.SendAsync(request, CancellationToken.None);

        await send.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Test_override_is_shared_by_all_handler_chains()
    {
        await using var provider = BuildProvider();
        provider.GetRequiredService<FrankfurterTestOverride>().Handler =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        using var first = new HttpMessageInvoker(BuildChain(provider), disposeHandler: true);
        using var second = new HttpMessageInvoker(BuildChain(provider), disposeHandler: true);

        foreach (var invoker in new[] { first, second })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.frankfurter.dev/v2/currencies");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Each_handler_chain_gets_its_own_primary_handler()
    {
        await using var provider = BuildProvider();

        var first = provider.GetRequiredService<FrankfurterTestableHandler>();
        var second = provider.GetRequiredService<FrankfurterTestableHandler>();

        first.Should().NotBeSameAs(second);
        provider.GetRequiredService<FrankfurterTestOverride>()
            .Should().BeSameAs(provider.GetRequiredService<FrankfurterTestOverride>());
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LupexWallet"] = "Host=localhost;Database=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExchangeRatesModule(configuration);
        return services.BuildServiceProvider();
    }

    private static HttpMessageHandler BuildChain(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(FrankfurterClient.HttpClientName);
        var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = FrankfurterClient.HttpClientName;
        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        return builder.Build();
    }
}
