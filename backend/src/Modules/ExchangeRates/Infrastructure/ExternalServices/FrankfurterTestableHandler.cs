namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// The single seam for replacing the real HTTP call to Frankfurter in integration tests
/// (docs/PROGRESS.md, W1.1 DoD: "no real network call"). In production/development
/// <see cref="TestOverride"/> is always null — the request goes through a plain
/// SocketsHttpHandler. Registered as a Singleton and used as the primary handler of the
/// named HttpClient "Frankfurter" (see ExchangeRatesModuleExtensions) — integration tests
/// obtain the same instance via WebApplicationFactory.Services and set TestOverride before
/// calling the endpoint.
/// </summary>
public sealed class FrankfurterTestableHandler : DelegatingHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? TestOverride { get; set; }

    public FrankfurterTestableHandler() : base(new SocketsHttpHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (TestOverride is not null)
        {
            return await TestOverride(request, cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
