using System.Net;
using FluentAssertions;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.ReferenceData;

/// <summary>
/// Atomicity of currency deletion (ADR-0001, §4; ADR-0002, §4): the currency row, its bulk-deleted latest
/// and historical rates and history coverage (ExecuteDelete bypasses the ChangeTracker) and the audit entry are one transaction.
/// A failure after the rates were already deleted (here: the audit insert) rolls everything back.
/// Technique as in RecordLifecycleAtomicityTests.
/// </summary>
public sealed class CurrencyDeletionAtomicityTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string RandomCode() => "AT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task DeleteCurrency_WhenAuditInsertFailsAfterRatesWereDeleted_KeepsCurrencyAndRates()
    {
        await AuthenticateAsync();
        var doomed = await Client.CreateCurrencyAsync(RandomCode(), "Удаляемая");
        var partner = await Client.CreateCurrencyAsync(RandomCode(), "Парная");
        await SeedRatesAsync(doomed.Id, partner.Id);
        await using var connection = await Fixture.OpenConnectionAsync();
        var ratesFilter = new (string, object)[] { ("id", doomed.Id) };
        var latestBefore = await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.latest_exchange_rates WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter);
        var historicalBefore = await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.historical_exchange_rates WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter);
        var coverageBefore = await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.historical_rate_coverage WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter);
        latestBefore.Should().Be(2);
        historicalBefore.Should().Be(2);
        coverageBefore.Should().Be(2);

        await connection.ExecuteAsync(
            "CREATE FUNCTION audit.fail_test_insert() RETURNS trigger AS $$ " +
            "BEGIN RAISE EXCEPTION 'test-induced audit failure'; END; $$ LANGUAGE plpgsql");
        await connection.ExecuteAsync(
            "CREATE TRIGGER fail_test_insert BEFORE INSERT ON audit.audit_entries " +
            "FOR EACH ROW WHEN (NEW.entity_type = 'Currency' AND NEW.action = 'Deleted') EXECUTE FUNCTION audit.fail_test_insert()");
        HttpStatusCode status;
        try
        {
            status = (await Client.DeleteAsync($"/api/v1/currencies/{doomed.Id}")).StatusCode;
        }
        finally
        {
            await connection.ExecuteAsync("DROP TRIGGER IF EXISTS fail_test_insert ON audit.audit_entries");
            await connection.ExecuteAsync("DROP FUNCTION IF EXISTS audit.fail_test_insert()");
        }

        status.Should().Be(HttpStatusCode.InternalServerError);
        (await connection.CountAsync("SELECT count(*) FROM reference_data.currencies WHERE id = @id", ratesFilter))
            .Should().Be(1, "the failed audit insert rolled the currency deletion back");
        (await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.latest_exchange_rates WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter))
            .Should().Be(latestBefore, "the bulk delete of latest rates was rolled back with the command");
        (await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.historical_exchange_rates WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter))
            .Should().Be(historicalBefore, "the bulk delete of historical rates was rolled back with the command");
        (await connection.CountAsync(
            "SELECT count(*) FROM exchange_rates.historical_rate_coverage WHERE from_currency_id = @id OR to_currency_id = @id", ratesFilter))
            .Should().Be(coverageBefore, "the bulk delete of the history coverage (ADR-0004 §9) was rolled back with the command");
    }

    private async Task SeedRatesAsync(Guid firstCurrencyId, Guid secondCurrencyId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExchangeRatesDbContext>();
        var first = new CurrencyId(firstCurrencyId);
        var second = new CurrencyId(secondCurrencyId);
        var fetchedAt = DateTimeOffset.UtcNow;
        dbContext.LatestExchangeRates.AddRange(
            ExchangeRateQuote.Create(first, second, new ExchangeRateValue(0.9m), fetchedAt),
            ExchangeRateQuote.Create(second, first, new ExchangeRateValue(1.1m), fetchedAt));
        dbContext.HistoricalExchangeRates.AddRange(
            HistoricalExchangeRate.Create(first, second, Today.AddDays(-5), new ExchangeRateValue(0.8m), fetchedAt),
            HistoricalExchangeRate.Create(second, first, Today.AddDays(-5), new ExchangeRateValue(1.2m), fetchedAt));
        dbContext.HistoricalRateCoverages.AddRange(
            HistoricalRateCoverage.Create(first, second, Today.AddDays(-10), Today, fetchedAt),
            HistoricalRateCoverage.Create(second, first, Today.AddDays(-10), Today, fetchedAt));
        await dbContext.SaveChangesAsync();
    }
}
