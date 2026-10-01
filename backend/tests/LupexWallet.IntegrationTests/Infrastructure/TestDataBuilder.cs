using System.Net.Http.Json;
using Npgsql;

namespace LupexWallet.IntegrationTests.Infrastructure;

// DTO-заглушки для десериализации ответов API в тестах — не связаны с *.Api-сборками
// модулей напрямую, чтобы не тянуть внутренние типы; используем только поля, нужные тестам.
public sealed record ReferenceItemResponse(Guid Id, string Name, bool IsActive);
public sealed record CurrencyResponse(Guid Id, string Code, string Name);
public sealed record AvailableCurrencyResponse(string Code, string Name, bool IsActive);
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

    private static string? _connectionString;

    /// <summary>Called once by <see cref="ApiTestFixture"/>: the builder seeds the available-currency catalog directly in the test database.</summary>
    internal static void UseDatabase(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Adds a code to <c>reference_data.available_currencies</c> directly (ADR-0004 §7: the list is
    /// normally loaded from the rate service). Existing rows are left untouched.
    /// </summary>
    public static async Task SeedAvailableCurrencyAsync(string code, string name = "Тестовая валюта")
    {
        if (_connectionString is null)
        {
            throw new InvalidOperationException("TestDataBuilder is not bound to a database: ApiTestFixture.InitializeAsync did not run.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO reference_data.available_currencies (code, name, refreshed_at) " +
            "VALUES (@code, @name, now()) ON CONFLICT (code) DO NOTHING";
        command.Parameters.AddWithValue("code", code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Makes the code available, then activates it through <c>POST /currencies</c>.</summary>
    public static async Task<CurrencyResponse> CreateCurrencyAsync(this HttpClient client, string code = "USD", string name = "Доллар США")
    {
        await SeedAvailableCurrencyAsync(code, name);
        var response = await client.PostAsJsonAsync("/api/v1/currencies", new { Code = code });
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
        Guid? currencyId = null,
        string? currencyCode = null)
    {
        // Имя типа кошелька уникально среди активных элементов (ux_wallet_types_active_name,
        // schema.md) — при автосоздании (walletTypeId не передан) генерируем уникальное имя,
        // иначе повторный вызов CreateWalletAsync в одном тесте упал бы с 500 (нарушение
        // уникального индекса) при попытке создать второй кошелек без явного walletTypeId.
        var effectiveWalletTypeId = walletTypeId ?? (await client.CreateWalletTypeAsync($"Тип-{Guid.NewGuid():N}")).Id;
        var effectiveCurrencyCode = currencyCode
            ?? (currencyId is { } id
                ? await client.GetCurrencyCodeAsync(id)
                : (await client.CreateCurrencyAsync(RandomCurrencyCode(), "Тестовая валюта")).Code);

        var response = await client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = name,
            WalletTypeId = effectiveWalletTypeId,
            CurrencyCode = effectiveCurrencyCode,
            InitialBalanceAmount = initialBalanceAmount,
            AccountingStartDate = accountingStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1),
        });
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    /// <summary>Resolves the code of an active currency by id (wallets are created by code, ADR-0004 §7).</summary>
    private static async Task<string> GetCurrencyCodeAsync(this HttpClient client, Guid currencyId)
    {
        string? cursor = null;
        do
        {
            var url = "/api/v1/currencies?limit=100" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await client.GetAsync(url);
            await response.EnsureSuccessWithBodyAsync();
            var page = (await response.Content.ReadFromJsonAsync<CurrencyPageResponse>())!;
            if (page.Data.FirstOrDefault(c => c.Id == currencyId) is { } found)
            {
                return found.Code;
            }

            cursor = page.Pagination.HasMore ? page.Pagination.NextCursor : null;
        }
        while (cursor is not null);

        throw new InvalidOperationException($"Currency {currencyId} is not among the active currencies.");
    }

    private static string RandomCurrencyCode() => "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed record CurrencyPageResponse(List<CurrencyResponse> Data, PageMeta Pagination);
    private sealed record PageMeta(string? NextCursor, bool HasMore);
    private sealed record OperationTypeResponse(Guid Id, string Name, Guid BehaviorKindId, bool IsActive);
}
