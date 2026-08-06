using System.Net.Http.Json;

namespace LupexWallet.IntegrationTests.Infrastructure;

// DTO-заглушки для десериализации ответов API в тестах — не связаны с *.Api-сборками
// модулей напрямую, чтобы не тянуть внутренние типы; используем только поля, нужные тестам.
public sealed record ReferenceItemResponse(Guid Id, string Name, bool IsActive);
public sealed record CurrencyResponse(Guid Id, string Code, string Name, bool IsActive);
public sealed record OperationBehaviorKindResponse(Guid Id, string Code, string Name);
public sealed record MoneyResponse(string Amount, Guid CurrencyId);
public sealed record WalletResponse(
    Guid Id, string Name, Guid WalletTypeId, string? PurposeDescription, Guid CurrencyId,
    MoneyResponse InitialBalance, DateOnly AccountingStartDate, MoneyResponse CurrentBalance,
    bool IncludeInTotal, bool IsPrimary, bool IsArchived, int DisplayOrder, string? Color, string? Icon,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Общие HTTP-хелперы для интеграционных тестов Operations/BalanceHistory/ReferenceData —
/// избегают дублирования подготовки справочников/кошельков в каждом тестовом классе.
/// Предполагает, что вызывающий тест уже вызвал <see cref="IntegrationTestBase.AuthenticateAsync"/>.
/// </summary>
public static class TestDataBuilder
{
    /// <summary>Как HttpResponseMessage.EnsureSuccessStatusCode(), но включает тело ответа в сообщение об ошибке — иначе Problem Details (detail с причиной) теряется и диагностировать падение теста сложно.</summary>
    private static async Task EnsureSuccessWithBodyAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode} от {response.RequestMessage?.RequestUri}: {body}");
        }
    }

    public static async Task<CurrencyResponse> CreateCurrencyAsync(this HttpClient client, string code = "USD", string name = "Доллар США")
    {
        var response = await client.PostAsJsonAsync("/api/v1/currencies", new { Code = code, Name = name });
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<CurrencyResponse>())!;
    }

    public static async Task<ReferenceItemResponse> CreateWalletTypeAsync(this HttpClient client, string name = "Наличные")
    {
        var response = await client.PostAsJsonAsync("/api/v1/wallet-types", new { Name = name });
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<ReferenceItemResponse>())!;
    }

    public static async Task<IReadOnlyList<OperationBehaviorKindResponse>> ListOperationBehaviorKindsAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/operation-behavior-kinds");
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<List<OperationBehaviorKindResponse>>())!;
    }

    public static async Task<Guid> GetBehaviorKindIdAsync(this HttpClient client, string code)
    {
        var kinds = await client.ListOperationBehaviorKindsAsync();
        return kinds.Single(k => k.Code == code).Id;
    }

    public static async Task<ReferenceItemResponse> CreateOperationTypeAsync(this HttpClient client, string behaviorKindCode, string? name = null)
    {
        var behaviorKindId = await client.GetBehaviorKindIdAsync(behaviorKindCode);
        var response = await client.PostAsJsonAsync(
            "/api/v1/operation-types", new { Name = name ?? $"{behaviorKindCode}-тип", BehaviorKindId = behaviorKindId });
        await response.EnsureSuccessWithBodyAsync();
        var body = await response.Content.ReadFromJsonAsync<OperationTypeResponse>();
        return new ReferenceItemResponse(body!.Id, body.Name, body.IsActive);
    }

    public static async Task<Guid> CreateOperationTypeIdAsync(this HttpClient client, string behaviorKindCode, string? name = null) =>
        (await client.CreateOperationTypeAsync(behaviorKindCode, name)).Id;

    public static async Task<WalletResponse> CreateWalletAsync(
        this HttpClient client,
        decimal initialBalanceAmount = 0m,
        DateOnly? accountingStartDate = null,
        string name = "Кошелек",
        Guid? walletTypeId = null,
        Guid? currencyId = null)
    {
        // Имя типа кошелька уникально среди активных элементов (ux_wallet_types_active_name,
        // schema.md) — при автосоздании (walletTypeId не передан) генерируем уникальное имя,
        // иначе повторный вызов CreateWalletAsync в одном тесте упал бы с 500 (нарушение
        // уникального индекса) при попытке создать второй кошелек без явного walletTypeId.
        var effectiveWalletTypeId = walletTypeId ?? (await client.CreateWalletTypeAsync($"Тип-{Guid.NewGuid():N}")).Id;
        var effectiveCurrencyId = currencyId ?? (await client.CreateCurrencyAsync(RandomCurrencyCode(), "Тестовая валюта")).Id;

        var response = await client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = name,
            WalletTypeId = effectiveWalletTypeId,
            CurrencyId = effectiveCurrencyId,
            InitialBalanceAmount = initialBalanceAmount,
            AccountingStartDate = accountingStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
        });
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    private static string RandomCurrencyCode() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record OperationTypeResponse(Guid Id, string Name, Guid BehaviorKindId, bool IsActive);
}
