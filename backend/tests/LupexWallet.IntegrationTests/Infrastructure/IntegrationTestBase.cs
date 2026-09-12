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

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Прикрепляет к <see cref="Client"/> Bearer-токен, полученный один раз на весь прогон
    /// коллекции (см. ApiTestFixture.Token) — НЕ логинится заново на каждый тест: /auth/login
    /// ограничен по частоте (5 попыток/минуту, единое окно на весь Host, RateLimiting.cs),
    /// и десятки тестов быстро исчерпали бы лимит, если бы каждый логинился отдельно.
    /// </summary>
    protected Task AuthenticateAsync()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
        return Task.CompletedTask;
    }
}
