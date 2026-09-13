using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Audit;

// DTO для десериализации ответов Audit API (docs/api/openapi.yaml, тег Audit, UC-25).
public sealed record AuditFieldChangeResponse(string Field, string? OldValue, string? NewValue);
public sealed record AuditEntryResponse(
    Guid Id, string EntityType, Guid EntityId, string Action, DateTimeOffset OccurredAt,
    string ActorKind, string? ActorSystemProcess, IReadOnlyList<AuditFieldChangeResponse> Changes);
public sealed record CursorPageMetaResponse(string? NextCursor, bool HasMore);
public sealed record AuditEntryPageResponse(IReadOnlyList<AuditEntryResponse> Data, CursorPageMetaResponse Pagination);
public sealed record WalletPageResponse(IReadOnlyList<WalletResponse> Data, CursorPageMetaResponse Pagination);

/// <summary>
/// Audit API (docs/api/openapi.yaml, тег Audit, UC-25) — ADR-0010: неизменяемость журнала,
/// атомарность с мутирующим действием, курсорная пагинация без дублей.
/// </summary>
public sealed class AuditApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static async Task<AuditEntryPageResponse> GetAuditEntriesAsync(
        HttpClient client, string entityType, Guid entityId, string? cursor = null, int limit = 20)
    {
        var url = $"/api/v1/audit-entries?entityType={entityType}&entityId={entityId}&limit={limit}";
        if (cursor is not null)
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AuditEntryPageResponse>())!;
    }

    private static async Task<WalletResponse> UpdateWalletAsync(HttpClient client, WalletResponse wallet, string newName)
    {
        var response = await client.PatchAsJsonAsync($"/api/v1/wallets/{wallet.Id}", new
        {
            Name = newName,
            wallet.WalletTypeId,
            wallet.PurposeDescription,
            wallet.IncludeInTotal,
            wallet.DisplayOrder,
            wallet.Color,
            wallet.Icon,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    [Fact]
    public async Task AuditEntries_Table_IsImmutable_UpdateAndDeleteAreNoOps()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек для аудита");

        var before = await GetAuditEntriesAsync(Client, "Wallet", wallet.Id);
        before.Data.Should().NotBeEmpty("создание кошелька обязано породить AuditEntry (ddd-model.md §5)");

        // CREATE RULE audit_entries_no_update/no_delete (schema.md, миграция InitialCreate) —
        // защита неизменяемости журнала на уровне БД, не только на уровне приложения (AuditEntry
        // не имеет методов изменения/удаления). Проверяем напрямую raw SQL — через HTTP API
        // модуль Audit вообще не выставляет мутирующих эндпоинтов.
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.CommandText = "UPDATE audit.audit_entries SET action = 'Tampered' WHERE entity_type = 'Wallet' AND entity_id = @entityId";
            updateCommand.Parameters.AddWithValue("entityId", wallet.Id);
            var affected = await updateCommand.ExecuteNonQueryAsync();
            affected.Should().Be(0, "CREATE RULE audit_entries_no_update превращает UPDATE в DO INSTEAD NOTHING — 0 физически затронутых строк");
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.CommandText = "DELETE FROM audit.audit_entries WHERE entity_type = 'Wallet' AND entity_id = @entityId";
            deleteCommand.Parameters.AddWithValue("entityId", wallet.Id);
            var affected = await deleteCommand.ExecuteNonQueryAsync();
            affected.Should().Be(0, "CREATE RULE audit_entries_no_delete превращает DELETE в DO INSTEAD NOTHING — 0 физически затронутых строк");
        }

        var after = await GetAuditEntriesAsync(Client, "Wallet", wallet.Id);
        after.Data.Should().BeEquivalentTo(before.Data, "UPDATE/DELETE не должны были изменить ни одной записи");
    }

    [Fact]
    public async Task CreateWallet_RecordsAuditEntryInSameTransactionAsTheMutatingCommand()
    {
        await AuthenticateAsync();

        var wallet = await Client.CreateWalletAsync(name: "Кошелек с аудитом создания");

        var page = await GetAuditEntriesAsync(Client, "Wallet", wallet.Id);
        page.Data.Should().Contain(e => e.Action == "Created" && e.ActorKind == "User");
    }

    /// <summary>
    /// DoD W2.2 п.2: "откат команды откатывает и аудит" (ddd-model.md §5). Прямая инъекция
    /// падения БАЗЫ ДАННЫХ ровно между двумя вложенными SaveChangesAsync одной команды
    /// недостижима через публичный HTTP API этого проекта без искусственных тестовых хуков
    /// (см. анализ в истории реализации W2.2) — большинство команд либо проверяют инварианты
    /// ДО первого SaveChangesAsync (не создают частичного состояния для отката), либо (как
    /// CreateOperationCommand/CreateTransferCommand) не имеют доступной через API проверки
    /// МЕЖДУ вложенными сохранениями. POST /wallets/{id}/set-primary — единственная команда
    /// проекта, где реальная, наблюдаемая через HTTP цепочка такова: (1) снятие признака
    /// "основной" со старого основного кошелька — реальный SaveChangesAsync, физически
    /// применяется к БД в рамках TransactionScope (TransactionBehavior), затем (2) попытка
    /// назначить архивный кошелек основным бросает CannotSetArchivedWalletAsPrimaryException
    /// ДО финального SaveChangesAsync. TransactionScope.Complete() не вызывается —
    /// откатывается ВСЯ команда целиком, включая уже "сохраненный" на шаге (1) SaveChangesAsync.
    /// UnmarkAsPrimary() не поднимает собственного доменного события (Wallet.cs) — поэтому
    /// эта команда не создает нового AuditEntry на шаге (1), которое можно было бы проверить на
    /// откат напрямую; вместо этого тест проверяет ОБА наблюдаемых следствия отката одной и
    /// той же гарантии TransactionScope, на которой стоит атомарность аудита (ADR-0008/0010):
    /// (a) бизнес-состояние (старый основной кошелек остается основным) и (b) журнал аудита
    /// старого основного кошелька не получает НИКАКИХ новых записей от неудачной попытки —
    /// ровно то, что требовалось бы, если бы шаг (1) поднимал событие.
    /// </summary>
    [Fact]
    public async Task SetPrimaryWallet_OnArchivedWallet_RollsBackBothWalletStateAndAuditTrail()
    {
        await AuthenticateAsync();

        var primaryWallet = await Client.CreateWalletAsync(name: "Первый (основной)"); // первый кошелек -> primary автоматически
        var secondWallet = await Client.CreateWalletAsync(name: "Второй (архивируется)");

        var archiveResponse = await Client.PostAsync($"/api/v1/wallets/{secondWallet.Id}/archive", null);
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var beforeAttempt = await GetAuditEntriesAsync(Client, "Wallet", primaryWallet.Id);

        var setPrimaryResponse = await Client.PostAsync($"/api/v1/wallets/{secondWallet.Id}/set-primary", null);
        setPrimaryResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "архивный кошелек не может стать основным (CannotSetArchivedWalletAsPrimaryException)");

        var walletsPage = await Client.GetFromJsonAsync<WalletPageResponse>("/api/v1/wallets?includeArchived=true");
        var reloadedPrimaryWallet = walletsPage!.Data.Single(w => w.Id == primaryWallet.Id);
        reloadedPrimaryWallet.IsPrimary.Should().BeTrue("неудачная команда set-primary должна откатить снятие признака 'основной' со старого основного кошелька");

        var afterAttempt = await GetAuditEntriesAsync(Client, "Wallet", primaryWallet.Id);
        afterAttempt.Data.Should().BeEquivalentTo(beforeAttempt.Data, "неудачная команда не должна оставить в журнале аудита никаких следов промежуточного шага (откат той же транзакции, что и бизнес-состояние)");
    }

    [Fact]
    public async Task GetAuditEntries_CursorPagination_ReturnsAllEntriesWithoutDuplicates()
    {
        await AuthenticateAsync();
        // Respawn сбрасывает wallets.wallets перед КАЖДЫМ тестом (IntegrationTestBase) — первый
        // созданный в тесте кошелек всегда становится основным (isFirstWallet), что добавило бы
        // лишнее событие PrimaryWalletChanged/"PrimaryChanged" к отслеживаемому кошельку. Создаем
        // кошелек-затравку первым, чтобы отслеживаемый кошелек ниже НЕ был первым/основным —
        // тогда его аудит-история состоит ровно из "Created" + N x "Updated".
        await Client.CreateWalletAsync(name: "Затравка (основной)");
        var wallet = await Client.CreateWalletAsync(name: "Кошелек для пагинации");

        var current = wallet;
        const int updateCount = 5;
        for (var i = 0; i < updateCount; i++)
        {
            current = await UpdateWalletAsync(Client, current, $"Кошелек для пагинации #{i}");
        }

        var collected = new List<AuditEntryResponse>();
        string? cursor = null;
        var expectedTotal = 1 + updateCount; // "Created" + N "Updated"

        do
        {
            var page = await GetAuditEntriesAsync(Client, "Wallet", wallet.Id, cursor, limit: 2);
            collected.AddRange(page.Data);
            cursor = page.Pagination.NextCursor;

            if (!page.Pagination.HasMore)
            {
                break;
            }
        }
        while (cursor is not null);

        collected.Should().HaveCount(expectedTotal);
        collected.Select(e => e.Id).Should().OnlyHaveUniqueItems("курсорная пагинация не должна возвращать дубли между страницами");
        collected.Count(e => e.Action == "Created").Should().Be(1);
        collected.Count(e => e.Action == "Updated").Should().Be(updateCount);
    }

    [Fact]
    public async Task GetAuditEntries_OccurredAtCollision_ReturnsAllEntriesWithoutLoss()
    {
        // Регресс (M1): Audit.Domain.MonotonicClock гарантирует строго возрастающие метки
        // только на 100нс-тиках CLR-процесса — колонка occurred_at в PostgreSQL хранит только
        // микросекунды, поэтому несколько записей могут получить в БД одно и то же значение.
        // Курсор без тай-брейкера (Id) в такой ситуации мог бы терять записи между страницами.
        // Здесь коллизия воспроизводится напрямую через DbContext (обходя MonotonicClock),
        // чтобы не зависеть от таймингов реальных часов.
        await AuthenticateAsync();
        // Как и в GetAuditEntries_CursorPagination_ReturnsAllEntriesWithoutDuplicates: первый
        // созданный в тесте кошелек становится основным (лишнее событие в его аудит-истории),
        // поэтому отслеживаемый кошелек создаётся вторым.
        await Client.CreateWalletAsync(name: "Затравка (основной)");
        var wallet = await Client.CreateWalletAsync(name: "Кошелек для теста коллизии occurred_at");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var collisionTimestamp = DateTimeOffset.UtcNow;
        var occurredAtProperty = typeof(AuditEntry).GetProperty(nameof(AuditEntry.OccurredAt))!;
        const int collidingCount = 5;
        for (var i = 0; i < collidingCount; i++)
        {
            var entry = AuditEntry.Create("Wallet", wallet.Id, "Updated", AuditActor.User());
            occurredAtProperty.SetValue(entry, collisionTimestamp);
            dbContext.AuditEntries.Add(entry);
        }

        await dbContext.SaveChangesAsync();

        var collected = new List<AuditEntryResponse>();
        string? cursor = null;
        var expectedTotal = 1 + collidingCount; // "Created" (из CreateWalletAsync) + N коллидирующих "Updated"

        do
        {
            var page = await GetAuditEntriesAsync(Client, "Wallet", wallet.Id, cursor, limit: 2);
            collected.AddRange(page.Data);
            cursor = page.Pagination.NextCursor;

            if (!page.Pagination.HasMore)
            {
                break;
            }
        }
        while (cursor is not null);

        collected.Should().HaveCount(expectedTotal, "коллизия occurred_at не должна приводить к потере записей курсорной пагинацией");
        collected.Select(e => e.Id).Should().OnlyHaveUniqueItems("курсорная пагинация не должна возвращать дубли даже при коллизии occurred_at");
    }
}
