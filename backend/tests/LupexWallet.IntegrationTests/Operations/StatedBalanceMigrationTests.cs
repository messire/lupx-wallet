using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.Operations.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Migration AddStatedBalanceCorrections (ADR-0003, §6) on rows in the former format. The shared test database is rolled
/// back to the previous migration, legacy 'Absolute' rows are seeded with SQL, and the migration is applied again —
/// exactly what happens to a database that already holds such rows. A test always leaves the database migrated to the
/// latest version. No other test touches the database meanwhile (one collection, run sequentially).
/// </summary>
public sealed class StatedBalanceMigrationTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string PreviousMigration = "20260930232038_AddRecordSoftDelete";
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    private IMigrator CreateMigrator(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<OperationsDbContext>().Database.GetService<IMigrator>();

    private async Task MigrateAsync(string? target = null)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        await CreateMigrator(scope).MigrateAsync(target);
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private sealed record Row(
        Guid Id, Guid? OperationTypeId, string? AdjustmentMode, decimal Amount, decimal AppliedDelta,
        bool IsCorrection, decimal? StatedBalance, bool IsDeleted, DateOnly Date);

    private static async Task<Dictionary<Guid, Row>> ReadRowsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, operation_type_id, adjustment_mode, amount, applied_delta_amount, is_correction, " +
            "stated_balance_amount, deleted_at IS NOT NULL, operation_date FROM operations.operations";
        var rows = new Dictionary<Guid, Row>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows[reader.GetGuid(0)] = new Row(
                reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDecimal(3), reader.GetDecimal(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.GetBoolean(7), DateOnly.FromDateTime(reader.GetDateTime(8)));
        }

        return rows;
    }

    // ---- Legacy data (the schema is at the previous migration: no is_correction / stated_balance_amount) ----

    private static Guid IdOf(int n) => Guid.Parse($"00000000-0000-0000-0000-{n:D12}");

    private static async Task InsertLegacyAsync(
        NpgsqlConnection connection, Guid id, Guid walletId, Guid? typeId, Guid currencyId, DateOnly date, string? mode,
        decimal amount, decimal appliedDelta, DateTimeOffset createdAt, DateTimeOffset? deletedAt = null) =>
        await ExecAsync(
            connection,
            "INSERT INTO operations.operations (id, wallet_id, operation_type_id, currency_id, operation_date, adjustment_mode, " +
            "created_at, updated_at, amount, applied_delta_amount, deleted_at) " +
            "VALUES (@id, @wallet, @type, @currency, @date, @mode, @createdAt, @createdAt, @amount, @delta, @deletedAt)",
            ("id", id), ("wallet", walletId), ("type", typeId), ("currency", currencyId), ("date", date), ("mode", mode),
            ("createdAt", createdAt), ("amount", amount), ("delta", appliedDelta), ("deletedAt", deletedAt));

    private sealed record Legacy(Guid WalletId, Guid OtherWalletId, Guid TypeId, Guid AdjustmentTypeId, Guid CurrencyId);

    /// <summary>One regular income, four kinds of former 'Absolute' rows and a 'Delta' adjustment; ids 1..N are fixed so tests can address rows.</summary>
    private async Task SeedLegacyRowsAsync(NpgsqlConnection connection, Legacy legacy)
    {
        var t0 = new DateTimeOffset(Today.AddDays(-30).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var (w, w2, type, adjType, currency) = (legacy.WalletId, legacy.OtherWalletId, legacy.TypeId, legacy.AdjustmentTypeId, legacy.CurrencyId);

        // 1: regular income, untouched.
        await InsertLegacyAsync(connection, IdOf(1), w, type, currency, Today.AddDays(-20), null, 500m, 500m, t0);
        // 2: the only Absolute on its date: becomes the correction (entered 350, former effect -550).
        await InsertLegacyAsync(connection, IdOf(2), w, adjType, currency, Today.AddDays(-10), "Absolute", 350m, -550m, t0.AddHours(1));
        // 3/4: two active Absolute rows on one date: the later one becomes the correction, the earlier one a Delta.
        await InsertLegacyAsync(connection, IdOf(3), w, adjType, currency, Today.AddDays(-8), "Absolute", 200m, 10m, t0.AddHours(2));
        await InsertLegacyAsync(connection, IdOf(4), w, adjType, currency, Today.AddDays(-8), "Absolute", 260m, 60m, t0.AddHours(3));
        // 5: a soft-deleted Absolute alone on its date: becomes a Delta, stays deleted.
        await InsertLegacyAsync(connection, IdOf(5), w, adjType, currency, Today.AddDays(-6), "Absolute", 999m, 5m, t0.AddHours(4), deletedAt: t0.AddHours(5));
        // 6/7: an active Absolute and a LATER deleted one on the same date: the active one becomes the correction.
        await InsertLegacyAsync(connection, IdOf(6), w, adjType, currency, Today.AddDays(-4), "Absolute", 70m, 7m, t0.AddHours(6));
        await InsertLegacyAsync(connection, IdOf(7), w, adjType, currency, Today.AddDays(-4), "Absolute", 80m, 8m, t0.AddHours(7), deletedAt: t0.AddHours(8));
        // 8/9: two active Absolute rows with the same created_at: the higher id wins the tie.
        await InsertLegacyAsync(connection, IdOf(8), w, adjType, currency, Today.AddDays(-2), "Absolute", 11m, 1m, t0.AddHours(9));
        await InsertLegacyAsync(connection, IdOf(9), w, adjType, currency, Today.AddDays(-2), "Absolute", 22m, 2m, t0.AddHours(9));
        // 10: a Delta adjustment, untouched.
        await InsertLegacyAsync(connection, IdOf(10), w, adjType, currency, Today.AddDays(-1), "Delta", -15m, -15m, t0.AddHours(10));
        // 11: another wallet, the same date as row 2: a separate partition, also becomes a correction.
        await InsertLegacyAsync(connection, IdOf(11), w2, adjType, currency, Today.AddDays(-10), "Absolute", 40m, 40m, t0.AddHours(1));
    }

    private async Task<Legacy> CreateLegacyContextAsync()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("MG" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var w = await Client.CreateWalletAsync(0m, Today.AddDays(-30), "Кошелек", walletType.Id, currency.Id);
        var w2 = await Client.CreateWalletAsync(0m, Today.AddDays(-30), "Второй", walletType.Id, currency.Id);
        var type = await Client.CreateOperationTypeIdAsync("Income");
        var adjType = await Client.CreateOperationTypeIdAsync("Adjustment");
        return new Legacy(w.Id, w2.Id, type, adjType, currency.Id);
    }

    /// <summary>Rolls back to the previous migration, seeds the legacy rows and applies the migration; the schema ends at the latest version.</summary>
    private async Task<Dictionary<Guid, Row>> SeedAndMigrateAsync(Legacy legacy, NpgsqlConnection connection)
    {
        await MigrateAsync(PreviousMigration);
        try
        {
            await SeedLegacyRowsAsync(connection, legacy);
        }
        finally
        {
            await MigrateAsync();
        }

        return await ReadRowsAsync(connection);
    }

    // ---- Up: conversion of the former rows ----

    [Fact]
    public async Task Up_ConvertsFormerAbsoluteRowsAsSpecified_AndKeepsEveryEffectOnTheBalance()
    {
        var legacy = await CreateLegacyContextAsync();
        await using var connection = await Fixture.OpenConnectionAsync();

        var rows = await SeedAndMigrateAsync(legacy, connection);

        rows.Values.Count(r => r.AdjustmentMode == "Absolute").Should().Be(0, "'Absolute' no longer exists");
        rows.Values.Where(r => r.IsCorrection).Select(r => r.Id).Should().BeEquivalentTo(
            [IdOf(2), IdOf(4), IdOf(6), IdOf(9), IdOf(11)],
            "per wallet and date the latest active Absolute row (created_at, then id) becomes the correction");

        // A correction: the entered value becomes the stated balance, the former effect stays until the reconciliation recalculates it.
        foreach (var (id, entered, effect) in new[] { (IdOf(2), 350m, -550m), (IdOf(4), 260m, 60m), (IdOf(6), 70m, 7m), (IdOf(9), 22m, 2m), (IdOf(11), 40m, 40m) })
        {
            var row = rows[id];
            row.StatedBalance.Should().Be(entered);
            row.Amount.Should().Be(effect, "amount = applied_delta_amount for a correction");
            row.AppliedDelta.Should().Be(effect);
            row.OperationTypeId.Should().BeNull();
            row.AdjustmentMode.Should().BeNull();
            row.IsDeleted.Should().BeFalse();
        }

        // The other former Absolute rows: a Delta adjustment with their effect, type and deletion kept.
        foreach (var (id, effect, deleted) in new[] { (IdOf(3), 10m, false), (IdOf(5), 5m, true), (IdOf(7), 8m, true), (IdOf(8), 1m, false) })
        {
            var row = rows[id];
            row.IsCorrection.Should().BeFalse();
            row.StatedBalance.Should().BeNull();
            row.AdjustmentMode.Should().Be("Delta");
            row.Amount.Should().Be(effect);
            row.AppliedDelta.Should().Be(effect);
            row.OperationTypeId.Should().Be(legacy.AdjustmentTypeId);
            row.IsDeleted.Should().Be(deleted);
        }

        // Rows that were never 'Absolute' are untouched.
        rows[IdOf(1)].Should().Be(new Row(IdOf(1), legacy.TypeId, null, 500m, 500m, false, null, false, Today.AddDays(-20)));
        rows[IdOf(10)].Should().Be(new Row(IdOf(10), legacy.AdjustmentTypeId, "Delta", -15m, -15m, false, null, false, Today.AddDays(-1)));

        // The effect on the balance is preserved exactly: applied_delta_amount is never touched by the conversion.
        rows.Values.Sum(r => r.AppliedDelta).Should().Be(500m - 550m + 10m + 60m + 5m + 7m + 8m + 1m + 2m - 15m + 40m);
    }

    [Fact]
    public async Task Up_DataConversionStatements_AreIdempotent()
    {
        var legacy = await CreateLegacyContextAsync();
        await using var connection = await Fixture.OpenConnectionAsync();
        var afterFirstRun = await SeedAndMigrateAsync(legacy, connection);

        // Run the very statements that ship in the migration once more.
        var statements = new AddStatedBalanceCorrections().UpOperations.OfType<SqlOperation>().Select(x => x.Sql).ToList();
        statements.Should().HaveCount(2, "the migration converts the data with two statements");
        foreach (var statement in statements)
        {
            await ExecAsync(connection, statement);
        }

        (await ReadRowsAsync(connection)).Should().BeEquivalentTo(afterFirstRun);
    }

    [Fact]
    public async Task Up_OnAnEmptyTable_Succeeds()
    {
        await MigrateAsync(PreviousMigration);
        await MigrateAsync();

        await using var connection = await Fixture.OpenConnectionAsync();
        (await connection.CountAsync("SELECT count(*) FROM operations.operations")).Should().Be(0);
    }

    // ---- The reconciliation heals what the migration leaves ----

    [Fact]
    public async Task ReconciliationAfterTheMigration_RecalculatesTheCorrectionAndHealsCurrentBalance()
    {
        var legacy = await CreateLegacyContextAsync();
        await using var connection = await Fixture.OpenConnectionAsync();
        await MigrateAsync(PreviousMigration);
        try
        {
            var t0 = new DateTimeOffset(Today.AddDays(-30).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var d1 = Today.AddDays(-20);
            var d2 = Today.AddDays(-10);
            await InsertLegacyAsync(connection, IdOf(1), legacy.WalletId, legacy.TypeId, legacy.CurrencyId, d1, null, 500m, 500m, t0);
            // Entered as 350 when the wallet balance was 900: the old formula froze the delta 350 - 900 = -550.
            await InsertLegacyAsync(connection, IdOf(2), legacy.WalletId, legacy.AdjustmentTypeId, legacy.CurrencyId, d2, "Absolute", 350m, -550m, t0.AddHours(1));
            await ExecAsync(connection, "UPDATE wallets.wallets SET current_balance_amount = -50 WHERE id = @id", ("id", legacy.WalletId));
        }
        finally
        {
            await MigrateAsync();
        }

        await Fixture.RunCorrectionsReconciliationAsync(legacy.WalletId);

        (await Fixture.CorrectionAmountAsync(legacy.WalletId, Today.AddDays(-10))).Should().Be(-150m, "350 stated - 500 by the operations");
        (await Client.GetWalletBalanceOnDateAsync(legacy.WalletId, Today.AddDays(-10))).Should().Be(350m, "the balance of the stated day is the stated one");
        (await Client.GetWalletBalanceAsync(legacy.WalletId)).Should().Be(350m, "CurrentBalance is brought back to the invariant");
        await Fixture.AssertWalletInvariantAsync(legacy.WalletId, "after the reconciliation");
        (await Client.GetSnapshotsAsync(legacy.WalletId, Today.AddDays(-10), Today.AddDays(-10))).Single().Balance.Amount.Should().Be("350");
        var correctionId = IdOf(2);
        var audit = await Client.GetAuditEntriesAsync("Operation", correctionId);
        audit.Should().ContainSingle(e => e.Action == "Recalculated" && e.ActorKind == "System");

        // A second pass has nothing left to do: no new audit entries.
        var auditBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");
        await Fixture.RunCorrectionsReconciliationAsync(legacy.WalletId);
        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(auditBefore);
    }

    // ---- Down ----

    [Fact]
    public async Task Down_WithCorrections_FailsWithAClearMessageAndLosesNothing()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(0m, Today.AddDays(-20));
        await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 350m);

        var act = () => MigrateAsync(PreviousMigration);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.ToString().Should().Contain("stated balances", "the guard names the reason instead of dropping the columns");
        await using var connection = await Fixture.OpenConnectionAsync();
        (await connection.CountAsync("SELECT count(*) FROM operations.operations WHERE is_correction")).Should().Be(1, "the correction survived");
        (await connection.CountAsync(
            "SELECT count(*) FROM operations.__ef_migrations_history WHERE migration_id LIKE '%AddStatedBalanceCorrections'"))
            .Should().Be(1, "the migration stays applied");
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(350m);
    }

    [Fact]
    public async Task Down_WithoutCorrections_RestoresTheFormerSchemaAndTheMigrationCanBeAppliedAgain()
    {
        await using var connection = await Fixture.OpenConnectionAsync();

        await MigrateAsync(PreviousMigration);
        try
        {
            (await connection.CountAsync(
                "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'operations' AND table_name = 'operations' " +
                "AND column_name IN ('is_correction', 'stated_balance_amount')")).Should().Be(0);
            await InsertLegacyAsync(connection, IdOf(1), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Today, "Absolute", 1m, 1m, DateTimeOffset.UtcNow);
            await ExecAsync(connection, "DELETE FROM operations.operations");
        }
        finally
        {
            await MigrateAsync();
        }

        (await connection.CountAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'operations' AND table_name = 'operations' " +
            "AND column_name IN ('is_correction', 'stated_balance_amount')")).Should().Be(2);
    }

    // ---- The constraints the migration adds ----

    private static string Insert(string columns, string values) =>
        $"INSERT INTO operations.operations (id, wallet_id, currency_id, operation_date, created_at, updated_at, amount, applied_delta_amount, {columns}) " +
        $"VALUES (gen_random_uuid(), @wallet, gen_random_uuid(), @date, now(), now(), 0, 0, {values})";

    [Theory]
    [InlineData("ux_operations_wallet_correction_date", "is_correction, stated_balance_amount", "true, 1")]
    [InlineData("ck_operations_correction_shape", "is_correction, stated_balance_amount, operation_type_id", "true, 1, gen_random_uuid()")]
    [InlineData("ck_operations_correction_shape", "is_correction, stated_balance_amount, deleted_at", "true, 1, now()")]
    [InlineData("ck_operations_correction_shape", "is_correction, stated_balance_amount, adjustment_mode", "true, 1, 'Delta'")]
    [InlineData("ck_operations_correction_stated_balance", "is_correction, operation_type_id", "true, NULL")]
    [InlineData("ck_operations_correction_stated_balance", "is_correction, stated_balance_amount, operation_type_id", "false, 1, gen_random_uuid()")]
    [InlineData("ck_operations_regular_type", "is_correction", "false")]
    [InlineData("ck_operations_adjustment_mode", "is_correction, operation_type_id, adjustment_mode", "false, gen_random_uuid(), 'Absolute'")]
    public async Task Constraints_RejectInvalidRows(string constraint, string columns, string values)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        var wallet = Guid.NewGuid();
        var date = Today.AddDays(-3);
        // A correction already exists on (wallet, date): the unique case collides with it, the others are rejected by their CHECK.
        await ExecAsync(connection, Insert("is_correction, stated_balance_amount", "true, 5"), ("wallet", wallet), ("date", date));

        var act = () => ExecAsync(connection, Insert(columns, values), ("wallet", wallet), ("date", date));

        var thrown = (await act.Should().ThrowAsync<PostgresException>()).Which;
        thrown.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task Constraints_AllowOneCorrectionPerWalletAndDateAlongsideRegularRows()
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        var wallet = Guid.NewGuid();
        var date = Today.AddDays(-3);

        await ExecAsync(connection, Insert("is_correction, stated_balance_amount", "true, 5"), ("wallet", wallet), ("date", date));
        await ExecAsync(connection, Insert("is_correction, stated_balance_amount", "true, 6"), ("wallet", wallet), ("date", date.AddDays(1)));
        await ExecAsync(connection, Insert("is_correction, stated_balance_amount", "true, 7"), ("wallet", Guid.NewGuid()), ("date", date));
        await ExecAsync(connection, Insert("is_correction, operation_type_id", "false, gen_random_uuid()"), ("wallet", wallet), ("date", date));
        await ExecAsync(connection, Insert("is_correction, operation_type_id", "false, gen_random_uuid()"), ("wallet", wallet), ("date", date));

        (await connection.CountAsync("SELECT count(*) FROM operations.operations WHERE is_correction")).Should().Be(3);
    }
}
