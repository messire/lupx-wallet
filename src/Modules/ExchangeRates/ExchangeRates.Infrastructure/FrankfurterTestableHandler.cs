namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Единственная точка подмены реального HTTP-вызова к Frankfurter в интеграционных тестах
/// (docs/PROGRESS.md, W1.1 DoD: "без реального сетевого вызова"). В production/разработке
/// <see cref="TestOverride"/> всегда null — запрос идёт через обычный SocketsHttpHandler.
/// Регистрируется как Singleton и используется как primary handler именованного
/// HttpClient "Frankfurter" (см. ExchangeRatesModuleExtensions) — интеграционные тесты
/// получают тот же экземпляр через WebApplicationFactory.Services и подставляют
/// TestOverride перед вызовом эндпоинта.
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
