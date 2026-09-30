using System.Net.Http.Headers;
using Xunit;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Базовый класс интеграционных тестов: общий HTTP-клиент на реальный API (WebApplicationFactory)
/// поверх реального Postgres (Testcontainers), с чистой БД перед каждым тестом (Respawn).
/// </summary>
[Collection(nameof(ApiTestCollection))]
public abstract class IntegrationTestBase(ApiTestFixture fixture) : IAsyncLifetime
{
    /// <summary>Пароль для локальной разработки/тестов — appsettings.Development.json, см. docs/PROGRESS.md.</summary>
    public const string DevPassword = "ChangeMe123!";

    /// <summary>Shared fixture; derived classes use it instead of capturing their own constructor parameter (CS9107).</summary>
    protected ApiTestFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Прикрепляет к <see cref="Client"/> Bearer-токен, полученный один раз на весь прогон
    /// коллекции (см. ApiTestFixture.Token) — НЕ логинится заново на каждый тест: /auth/login
    /// ограничен по частоте (5 попыток/минуту, единое окно на весь Host, RateLimiting.cs),
    /// и десятки тестов быстро исчерпали бы лимит, если бы каждый логинился отдельно.
    /// </summary>
    protected Task AuthenticateAsync()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.Token);
        return Task.CompletedTask;
    }
}
