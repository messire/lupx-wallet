using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.Api.Auth;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Auth;

/// <summary>
/// Смоук-тест на реальном стеке (Testcontainers Postgres + все EF-миграции + весь Host) —
/// подтверждает, что интеграционная инфраструктура (Фаза 0 плана автономного конвейера,
/// docs/PROGRESS.md) поднимается и работает end-to-end. Дальнейшее покрытие
/// (Wallets/ReferenceData/Operations/BalanceHistory через реальные HTTP-вызовы) — задача
/// tdd-test-engineer.
/// </summary>
public sealed class AuthTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Login_WithValidDevPassword_ReturnsToken()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("ChangeMe123!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.Token.Should().NotBeNullOrWhiteSpace();
        body.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("wrong-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wallets_WithoutToken_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/wallets");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
