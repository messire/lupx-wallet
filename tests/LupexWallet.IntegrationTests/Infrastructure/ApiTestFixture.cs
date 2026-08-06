using System.Net.Http.Json;
using LupexWallet.Api.Auth;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Поднимает один контейнер Postgres 16 и один WebApplicationFactory на весь коллекшн
/// тестов (не на каждый тест — дорого). Между тестами данные сбрасываются через Respawn
/// (см. <see cref="IntegrationTestBase"/>), схема остаётся смигрированной один раз.
/// </summary>
public sealed class ApiTestFixture : IAsyncLifetime
{
    private static readonly string[] Schemas = ["wallets", "reference_data", "operations", "balance_history", "exchange_rates"];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .WithDatabase("lupex_wallet")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _connectionString = null!;
    private Respawner _respawner = null!;

    public LupexWalletApiFactory Factory { get; private set; } = null!;

    /// <summary>Нужен тестам, которые проверяют физические ограничения БД напрямую (например, CREATE RULE audit.audit_entries_no_update/no_delete) — не выразимо через HTTP API.</summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// Токен, полученный один раз на весь прогон коллекции тестов — /auth/login защищен
    /// ограничением частоты (RateLimiting.cs: 5 попыток/минуту, единое на весь Host,
    /// docs/api/api-design.md), поэтому каждый тест НЕ должен логиниться заново (десятки
    /// тестов быстро исчерпали бы лимит и начали получать 429) — см. IntegrationTestBase.AuthenticateAsync.
    /// </summary>
    public string Token { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        Factory = new LupexWalletApiFactory(_connectionString);
        await Factory.MigrateAllAsync();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = Schemas,
            TablesToIgnore =
            [
                .. Schemas.Select(schema => new Respawn.Graph.Table(schema, "__ef_migrations_history")),
                // Системный справочник (ddd-model.md §2.3): 4 строки сеются один раз миграцией,
                // не пользовательские данные — не должен затираться Respawn между тестами
                // (найдено при подключении покрытия тестами: без исключения GET
                // /operation-behavior-kinds возвращал бы 0 строк начиная со второго теста).
                new Respawn.Graph.Table("reference_data", "operation_behavior_kinds"),
            ],
        });

        using var loginClient = Factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(IntegrationTestBase.DevPassword));
        loginResponse.EnsureSuccessStatusCode();
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Token = body!.Token;
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        // ADR-0010 ("Тестовая инфраструктура: audit.audit_entries и Respawn"): схема audit
        // намеренно не входит в SchemasToInclude — CREATE RULE ... ON DELETE ... DO INSTEAD
        // NOTHING (schema.md) превращает DELETE, который генерирует Respawn, в no-op, поэтому
        // Respawn не может сбросить эту таблицу. TRUNCATE не перехватывается CREATE RULE
        // (PostgreSQL не поддерживает ON TRUNCATE для RULE) и очищает её как обычно.
        await using var truncateCommand = connection.CreateCommand();
        truncateCommand.CommandText = "TRUNCATE TABLE audit.audit_entries;";
        await truncateCommand.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(ApiTestCollection))]
public sealed class ApiTestCollection : ICollectionFixture<ApiTestFixture>;
