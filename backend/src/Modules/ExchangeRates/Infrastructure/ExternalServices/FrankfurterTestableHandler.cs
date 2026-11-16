namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// The single seam for replacing the real HTTP call to Frankfurter in integration tests
/// (no real network call). Holds only the override delegate and is registered as a Singleton:
/// integration tests obtain the same instance via WebApplicationFactory.Services and set
/// <see cref="Handler"/> before calling the endpoint. In production/development it is always null.
/// </summary>
public sealed class FrankfurterTestOverride
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Handler { get; set; }
}

/// <summary>
/// Primary handler of the named HttpClient "Frankfurter" (see ExchangeRatesModuleExtensions).
/// Registered as Transient: IHttpClientFactory rotates handler chains (HandlerLifetime, 2 min by
/// default) and disposes the expired chain together with its primary handler, so this handler
/// must never be a shared Singleton — otherwise every request after the first rotation fails with
/// ObjectDisposedException (production incident 2026-11-16, see docs/PROGRESS.md). Without a test
/// override the request goes through its own SocketsHttpHandler.
/// </summary>
public sealed class FrankfurterTestableHandler(FrankfurterTestOverride testOverride)
    : DelegatingHandler(new SocketsHttpHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var overrideHandler = testOverride.Handler;
        if (overrideHandler is not null)
        {
            return await overrideHandler(request, cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
