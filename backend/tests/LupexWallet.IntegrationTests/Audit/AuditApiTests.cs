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
/// Audit API (docs/api/openapi.yaml, тег Audit, UC-25) — ADR-0006: неизменяемость журнала,
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
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
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
    /// Откат команды откатывает и аудит (ddd-model.md §5, ADR-0001 §4). Прямая инъекция
    /// падения БАЗЫ ДАННЫХ ровно между двумя вложенными SaveChangesAsync одной команды
    /// недостижима через публичный HTTP API этого проекта без искусственных тестовых хуков:
    /// большинство команд либо проверяют инварианты
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
    /// той же гарантии TransactionScope, на которой стоит атомарность аудита (ADR-0001/ADR-0006):
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

    // ---- Soft delete / restore of operations and transfers (ADR-0002, §3, §5; ADR-0006) ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static readonly TimeSpan MicrosecondPrecision = TimeSpan.FromTicks(10);

    private static DateTimeOffset ParseInstant(string? value) =>
        DateTimeOffset.Parse(value!, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<Guid> CreateIncomeAsync(Guid walletId, Guid incomeTypeId, decimal amount, DateOnly date) =>
        await Client.CreateOperationReturningIdAsync(walletId, incomeTypeId, amount, date);

    [Fact]
    public async Task DeleteAndRestoreOperation_RecordDeletedAndRestoredEntriesWithDeletedAtDiff()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await CreateIncomeAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));

        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = (await Client.GetAuditEntriesAsync("Operation", operationId)).OrderBy(e => e.OccurredAt).ToList();
        entries.Select(e => e.Action).Should().Equal("Created", "Deleted", "Restored");

        var deleted = entries.Single(e => e.Action == "Deleted");
        deleted.ActorKind.Should().Be("User");
        var deletedChange = deleted.Changes.Should().ContainSingle(
            "soft deletion is a Modified aggregate: the diff holds only DeletedAt, not a full row snapshot").Subject;
        deletedChange.Field.Should().Be("DeletedAt");
        deletedChange.OldValue.Should().BeNull();
        deletedChange.NewValue.Should().NotBeNullOrEmpty();

        var restored = entries.Single(e => e.Action == "Restored");
        var restoredChange = restored.Changes.Should().ContainSingle().Subject;
        restoredChange.Field.Should().Be("DeletedAt");
        // Deleted holds the in-memory value (100 ns ticks), Restored the value read back from timestamptz (microseconds).
        ParseInstant(restoredChange.OldValue).Should().BeCloseTo(ParseInstant(deletedChange.NewValue), MicrosecondPrecision,
            "Restored reverts exactly the value that Deleted set");
        restoredChange.NewValue.Should().BeNull();
    }

    [Fact]
    public async Task DeleteOperation_PastDate_AlsoRecordsWalletBalanceChangedAndSnapshotUpdatedEntries()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await CreateIncomeAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        var balanceChangesBefore = (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Count(e => e.Action == "BalanceChanged");
        var snapshotUpdatesBefore = (await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id)).Count(e => e.Action == "SnapshotUpdated");

        await Client.DeleteAsync($"/api/v1/operations/{operationId}");

        (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Count(e => e.Action == "BalanceChanged")
            .Should().Be(balanceChangesBefore + 1, "the reversed delta changes CurrentBalance");
        var snapshotUpdates = (await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id)).Where(e => e.Action == "SnapshotUpdated").ToList();
        snapshotUpdates.Count.Should().Be(snapshotUpdatesBefore + 4, "snapshots from the operation date through today (4 days) change from 1300 to 1000");
    }

    [Fact]
    public async Task DeleteAndRestoreTransfer_RecordTransferAndBothLegEntriesWithTheSameDeletedAt()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("AU" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(500m, Today.AddDays(-10), "Источник", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(100m, Today.AddDays(-10), "Назначение", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = Today.AddDays(-4),
        });
        transferResponse.EnsureSuccessStatusCode();
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>());
        var transferId = transfer.GetProperty("id").GetGuid();
        var sourceLegId = transfer.GetProperty("sourceOperationId").GetGuid();
        var targetLegId = transfer.GetProperty("targetOperationId").GetGuid();

        (await Client.DeleteAsync($"/api/v1/transfers/{transferId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.PostAsync($"/api/v1/transfers/{transferId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var transferEntries = (await Client.GetAuditEntriesAsync("Transfer", transferId)).OrderBy(e => e.OccurredAt).ToList();
        transferEntries.Select(e => e.Action).Should().Equal("Created", "Deleted", "Restored");
        var transferDeleted = transferEntries.Single(e => e.Action == "Deleted").Changes.Should().ContainSingle().Subject;
        transferDeleted.Field.Should().Be("DeletedAt");
        transferDeleted.OldValue.Should().BeNull();
        transferDeleted.NewValue.Should().NotBeNullOrEmpty();
        var transferRestored = transferEntries.Single(e => e.Action == "Restored").Changes.Should().ContainSingle().Subject;
        transferRestored.Field.Should().Be("DeletedAt");
        ParseInstant(transferRestored.OldValue).Should().BeCloseTo(ParseInstant(transferDeleted.NewValue), MicrosecondPrecision);
        transferRestored.NewValue.Should().BeNull();

        foreach (var legId in new[] { sourceLegId, targetLegId })
        {
            var legEntries = (await Client.GetAuditEntriesAsync("Operation", legId)).OrderBy(e => e.OccurredAt).ToList();
            legEntries.Select(e => e.Action).Should().Equal("Created", "Deleted", "Restored");
            legEntries.Single(e => e.Action == "Deleted").Changes.Should().ContainSingle(c => c.Field == "DeletedAt" && c.NewValue != null && ParseInstant(c.NewValue) == ParseInstant(transferDeleted.NewValue),
                "the legs share the transfer's DeletedAt");
        }
    }

    [Fact]
    public async Task RejectedDeleteOfAlreadyDeletedOperation_DoesNotAddAuditEntries()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await CreateIncomeAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        await Client.DeleteAsync($"/api/v1/operations/{operationId}");
        var operationEntries = (await Client.GetAuditEntriesAsync("Operation", operationId)).Count;
        var walletEntries = (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Count;
        var snapshotEntries = (await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id)).Count;

        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.PostAsync($"/api/v1/operations/{Guid.NewGuid()}/restore", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Client.GetAuditEntriesAsync("Operation", operationId)).Should().HaveCount(operationEntries);
        (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Should().HaveCount(walletEntries);
        (await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id)).Should().HaveCount(snapshotEntries);
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

        using var scope = Fixture.Factory.Services.CreateScope();
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

    // ---- Balance corrections (ADR-0003, §6; ADR-0006) ----

    [Fact]
    public async Task StatedBalance_RecordsCreatedUpdatedAndRecalculatedEntriesWithAmountDiffsAndUserActor()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var day = Today.AddDays(-5);
        var incomeId = await CreateIncomeAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));

        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, day, 350m);
        await Client.SetStatedBalanceAsync(wallet.Id, day, 400m);
        (await Client.PatchAsJsonAsync($"/api/v1/operations/{incomeId}", new
        {
            WalletId = wallet.Id, OperationTypeId = incomeTypeId, Amount = 320m, OperationDate = Today.AddDays(-10),
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = (await Client.GetAuditEntriesAsync("Operation", correction.Id)).OrderBy(e => e.OccurredAt).ToList();

        entries.Select(e => e.Action).Should().Equal("Created", "Updated", "Recalculated");
        entries.Should().OnlyContain(e => e.ActorKind == "User");
        var created = entries[0];
        created.Changes.Should().Contain(c => c.NewValue == "350", "the first statement records the full row, the stated balance included");
        created.Changes.Should().Contain(c => c.NewValue == "50", "and the calculated amount");
        var updated = entries[1];
        updated.Changes.Should().Contain(c => c.OldValue == "350" && c.NewValue == "400");
        var recalculated = entries[2];
        recalculated.Changes.Should().Contain(c => c.OldValue == "100" && c.NewValue == "80", "the amount shrank by the +20 edit");
        recalculated.Changes.Should().NotContain(c => c.OldValue == "400" || c.NewValue == "400", "the stated balance is untouched by the system");
    }

    [Fact]
    public async Task OneCommandChangingCorrections_RecordsExactlyOneRecalculatedEntryPerChangedCorrection()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var (_, first) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-10), 100m);
        var (_, second) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 500m);

        // +30 before both dates: the first correction drops from 100 to 70, the second one stays 400 (the first absorbs the whole +30).
        await CreateIncomeAsync(wallet.Id, incomeTypeId, 30m, Today.AddDays(-15));

        (await Client.GetAuditEntriesAsync("Operation", first.Id)).Count(e => e.Action == "Recalculated").Should().Be(1);
        (await Client.GetAuditEntriesAsync("Operation", second.Id)).Count(e => e.Action == "Recalculated").Should().Be(0,
            "the second correction is unchanged: the first one absorbs the whole +30");
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }

    [Fact]
    public async Task ReplacingAStatedBalance_RecordsASingleUpdatedEntryNotAnAdditionalRecalculatedOne()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));

        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 350m);
        await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 360m);

        (await Client.GetAuditEntriesAsync("Operation", correction.Id)).Select(e => e.Action)
            .Should().BeEquivalentTo("Created", "Updated");
    }

    [Fact]
    public async Task ReconciliationOfAStaleCorrection_RecordsRecalculatedUnderTheSystemActor()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await CreateIncomeAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));
        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 350m);
        await using (var connection = await Fixture.OpenConnectionAsync())
        {
            // The state an older format leaves behind: a stale amount and a stale wallet balance.
            await connection.ExecuteAsync(
                $"UPDATE operations.operations SET amount = 0, applied_delta_amount = 0 WHERE id = '{correction.Id}'");
            await connection.ExecuteAsync($"UPDATE wallets.wallets SET current_balance_amount = 300 WHERE id = '{wallet.Id}'");
        }

        await Fixture.RunCorrectionsReconciliationAsync(wallet.Id);

        var recalculated = (await Client.GetAuditEntriesAsync("Operation", correction.Id)).Single(e => e.Action == "Recalculated");
        recalculated.ActorKind.Should().Be("System");
        recalculated.ActorSystemProcess.Should().Be("StatedBalanceTestReconciliation");
        recalculated.Changes.Should().Contain(c => c.OldValue == "0" && c.NewValue == "50");
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(350m);
    }

    [Fact]
    public async Task DeletingAWalletWithStatedBalances_RecordsADeletedEntryWithTheFullSnapshotForEachCorrection()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20), name: "С заявленными балансами");
        var (_, first) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-10), 350m);
        var (_, second) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 600m);

        (await Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var (correction, stated) in new[] { (first, "350"), (second, "600") })
        {
            var entries = await Client.GetAuditEntriesAsync("Operation", correction.Id);
            var deleted = entries.Should().ContainSingle(e => e.Action == "Deleted").Subject;
            deleted.ActorKind.Should().Be("User");
            deleted.Changes.Should().Contain(c => c.OldValue == stated && c.NewValue == null, "the snapshot holds the stated balance");
            deleted.Changes.Count.Should().BeGreaterThan(3, "a full snapshot of the row, not a single field");
        }

        (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Should().Contain(e => e.Action == "Deleted");
    }
}
