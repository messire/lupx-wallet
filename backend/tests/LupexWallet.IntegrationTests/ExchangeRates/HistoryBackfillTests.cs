using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.IntegrationTests.Reporting;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.ExchangeRates;

/// <summary>
/// The background pass (refresh, then BackfillExchangeRateHistoryCommand) against the real database and FrankfurterFake
/// (ADR-0004 §9, PROGRESS R11/R12): the period reaches the earliest accounting start date of all wallets (archived ones
/// included), the coverage table records progress, an empty chunk closes the left border, 422 skips a pair, an unavailable
/// source keeps the next pass able to continue, the right edge is re-read with an upsert, a change of the primary wallet
/// reloads the history to the new currency, deleting a currency removes its coverage, and each pass leaves one audit entry.
/// </summary>
public sealed class HistoryBackfillTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<T> WithDbAsync<T>(Func<ExchangeRatesDbContext, Task<T>> read)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ExchangeRatesDbContext>());
    }

    private IReadOnlyList<FrankfurterRatesRequest> PeriodRequests => _fake.RatesRequests.Where(r => r.From is not null).ToList();

    private async Task<(Guid Eur, Guid Usd)> CreateEurPrimaryAndUsdWalletsAsync(int eurStartDaysAgo, int usdStartDaysAgo)
    {
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной", accountingStartDate: Today.AddDays(-eurStartDaysAgo));
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD", accountingStartDate: Today.AddDays(-usdStartDaysAgo));
        return (eur.Id, usd.Id);
    }

    [Fact]
    public async Task Pass_PeriodStartsSevenDaysBeforeTheEarliestStartDateOfAnyWalletIncludingArchived()
    {
        await AuthenticateAsync();
        var (_, usd) = await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 10, usdStartDaysAgo: 20);
        var archived = await Client.CreateWalletAsync(currencyId: usd, name: "Архивный", accountingStartDate: Today.AddDays(-100));
        (await Client.PostAsync($"/api/v1/wallets/{archived.Id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var request = PeriodRequests.Should().ContainSingle().Subject;
        request.Base.Should().Be("USD");
        request.Quote.Should().Be("EUR");
        request.From.Should().Be(Today.AddDays(-107), "the archived wallet history is still shown, so its start counts");
        request.To.Should().Be(Today);
    }

    [Fact]
    public async Task Pass_WritesHistoryAndCoverageAndASecondPassRequestsNoPeriod()
    {
        await AuthenticateAsync();
        await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 30, usdStartDaysAgo: 30);

        var (_, backfill) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        backfill.Loaded.Should().ContainSingle();
        var coverage = await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().SingleAsync());
        coverage.CoveredFrom.Should().Be(Today.AddDays(-37));
        coverage.CoveredTo.Should().Be(Today);
        var dates = await WithDbAsync(db => db.HistoricalExchangeRates.AsNoTracking().Select(r => r.RateDate).ToListAsync());
        dates.Should().OnlyHaveUniqueItems();
        dates.Should().OnlyContain(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday, "the fake serves business days only");
        dates.Min().Should().BeOnOrAfter(Today.AddDays(-37));
        _fake.ResetCounters();

        var (_, second) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        PeriodRequests.Should().BeEmpty();
        second.Loaded.Should().BeEmpty();
        (await WithDbAsync(db => db.HistoricalExchangeRates.CountAsync())).Should().Be(dates.Count, "no duplicates");
    }

    [Fact]
    public async Task Pass_CurrencyWithDataStartingLater_EmptyChunkClosesTheLeftBorderAndNothingIsAskedAgain()
    {
        await AuthenticateAsync();
        await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 400, usdStartDaysAgo: 400);
        _fake.SetDataStart("USD", Today.AddDays(-100));

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        PeriodRequests.Should().HaveCount(2, "the first chunk has data, the second (older than the data start) is empty");
        var coverage = await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().SingleAsync());
        coverage.CoveredFrom.Should().Be(Today.AddDays(-407), "an empty chunk means the service has nothing older: the border is reached");
        (await WithDbAsync(db => db.HistoricalExchangeRates.MinAsync(r => r.RateDate))).Should().BeOnOrAfter(Today.AddDays(-100));
        _fake.ResetCounters();

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        PeriodRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Pass_UnsupportedCurrency_IsSkippedWithoutFailureAndOthersAreLoaded()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        var xyz = await Client.CreateCurrencyAsync("XYZ", "Unknown");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной", accountingStartDate: Today.AddDays(-20));
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD", accountingStartDate: Today.AddDays(-20));
        await Client.CreateWalletAsync(currencyId: xyz.Id, name: "XYZ", accountingStartDate: Today.AddDays(-20));
        _fake.SetRatesFailureFor("XYZ", FrankfurterFakeFailure.Unprocessable);

        var (refresh, backfill) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        refresh.HadFailures.Should().BeFalse();
        backfill.HadFailures.Should().BeFalse("422 is a permanent property of the pair");
        backfill.Loaded.Should().ContainSingle(l => l.FromCurrencyId.Value == usd.Id);
        (await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().ToListAsync()))
            .Should().ContainSingle().Which.FromCurrencyId.Value.Should().Be(usd.Id);
    }

    [Fact]
    public async Task Pass_SourceUnavailable_ReportsFailureStoresNothingAndTheNextPassLoads()
    {
        await AuthenticateAsync();
        await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 20, usdStartDaysAgo: 20);
        _fake.RatesFailure = FrankfurterFakeFailure.ServerError;

        var (_, failed) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        failed.HadFailures.Should().BeTrue();
        failed.Loaded.Should().BeEmpty();
        (await WithDbAsync(db => db.HistoricalRateCoverages.CountAsync())).Should().Be(0);

        _fake.RatesFailure = FrankfurterFakeFailure.None;
        var (_, recovered) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        recovered.HadFailures.Should().BeFalse();
        recovered.Loaded.Should().ContainSingle();
        (await WithDbAsync(db => db.HistoricalRateCoverages.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Pass_CoverageEndsBeforeToday_RereadsTheRightEdgeWithAnUpsertAndNoDuplicates()
    {
        await AuthenticateAsync();
        await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 30, usdStartDaysAgo: 30);
        _fake.RateForDate = (_, _, _) => 1.0m;
        await Fixture.RunExchangeRatesBackgroundPassAsync();
        var rowsBefore = await WithDbAsync(db => db.HistoricalExchangeRates.CountAsync());
        await ExecuteSqlAsync($"UPDATE exchange_rates.historical_rate_coverage SET covered_to = '{Today.AddDays(-3):yyyy-MM-dd}'");
        _fake.ResetCounters();
        _fake.RateForDate = (_, _, _) => 2.0m;

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var request = PeriodRequests.Should().ContainSingle("one request per pair: covered_to − 7 … today").Subject;
        request.From.Should().Be(Today.AddDays(-10));
        request.To.Should().Be(Today);
        var rows = await WithDbAsync(db => db.HistoricalExchangeRates.AsNoTracking().ToListAsync());
        rows.Count.Should().BeGreaterThanOrEqualTo(rowsBefore);
        rows.Select(r => r.RateDate).Should().OnlyHaveUniqueItems("the (from, to, date) key is upserted, never duplicated");
        rows.Where(r => r.RateDate >= Today.AddDays(-10)).Should().OnlyContain(r => r.Rate.Rate == 2.0m, "refined rates replace the stored ones");
        rows.Where(r => r.RateDate < Today.AddDays(-10)).Should().OnlyContain(r => r.Rate.Rate == 1.0m);
        var coverage = await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().SingleAsync());
        coverage.CoveredTo.Should().Be(Today);
    }

    [Fact]
    public async Task Pass_AfterTheSwitchOfThePrimaryWallet_LoadsHistoryToTheNewCurrencyAndDropsPairsToTheOldOne()
    {
        await AuthenticateAsync();
        var (eur, usd) = await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 30, usdStartDaysAgo: 30);
        var gbp = await Client.CreateCurrencyAsync("GBP", "Pound");
        var gbpWallet = await Client.CreateWalletAsync(currencyId: gbp.Id, name: "GBP", accountingStartDate: Today.AddDays(-30));
        await Fixture.RunExchangeRatesBackgroundPassAsync();
        (await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().ToListAsync()))
            .Should().OnlyContain(c => c.ToCurrencyId.Value == eur).And.HaveCount(2);

        (await Client.PostAsync($"/api/v1/wallets/{gbpWallet.Id}/set-primary", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        _fake.ResetCounters();
        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var coverage = await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().ToListAsync());
        coverage.Should().OnlyContain(c => c.ToCurrencyId.Value == gbp.Id);
        coverage.Select(c => c.FromCurrencyId.Value).Should().BeEquivalentTo([eur, usd]);
        var history = await WithDbAsync(db => db.HistoricalExchangeRates.AsNoTracking().ToListAsync());
        history.Should().NotBeEmpty().And.OnlyContain(r => r.ToCurrencyId.Value == gbp.Id, "history to the former primary currency is deleted at the end of the pass");
        var latest = await WithDbAsync(db => db.LatestExchangeRates.AsNoTracking().ToListAsync());
        latest.Should().NotBeEmpty().And.OnlyContain(r => r.ToCurrencyId.Value == gbp.Id);
        PeriodRequests.Select(r => (r.Base, r.Quote)).Should().BeEquivalentTo([("EUR", "GBP"), ("USD", "GBP")]);
    }

    [Fact]
    public async Task DeleteCurrency_RemovesItsCoverageAndHistory()
    {
        await AuthenticateAsync();
        await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 20, usdStartDaysAgo: 20);
        var gbp = await Client.CreateCurrencyAsync("GBP", "Pound");
        await Fixture.RunExchangeRatesBackgroundPassAsync();
        (await WithDbAsync(db => db.HistoricalRateCoverages.CountAsync())).Should().Be(2);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{gbp.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var coverage = await WithDbAsync(db => db.HistoricalRateCoverages.AsNoTracking().ToListAsync());
        coverage.Should().ContainSingle().Which.FromCurrencyId.Value.Should().NotBe(gbp.Id);
        var gbpId = new CurrencyId(gbp.Id);
        (await WithDbAsync(db => db.HistoricalExchangeRates.CountAsync(r => r.FromCurrencyId == gbpId || r.ToCurrencyId == gbpId)))
            .Should().Be(0);
    }

    [Fact]
    public async Task Pass_LeavesOneHistoryLoadedAuditEntryWithTheSystemActorAndNoneWhenNothingIsLoaded()
    {
        await AuthenticateAsync();
        var (eur, usd) = await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 20, usdStartDaysAgo: 20);

        await Fixture.RunExchangeRatesBackgroundPassAsync();
        var afterFirst = (await Client.GetAuditEntriesAsync("ExchangeRateQuote", Guid.Empty)).Where(e => e.Action == "HistoryLoaded").ToList();
        await Fixture.RunExchangeRatesBackgroundPassAsync();
        var afterSecond = (await Client.GetAuditEntriesAsync("ExchangeRateQuote", Guid.Empty)).Where(e => e.Action == "HistoryLoaded").ToList();

        var entry = afterFirst.Should().ContainSingle("one record per pass").Subject;
        entry.ActorKind.Should().Be("System");
        entry.EntityId.Should().Be(Guid.Empty);
        entry.Changes.Should().ContainSingle().Which.Should().Match<AuditFieldChangeResponse>(c =>
            c.Field == $"{usd}->{eur}"
            && c.NewValue == $"{Today.AddDays(-27).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}..{Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        afterSecond.Should().HaveCount(1, "the second pass loaded nothing and failed nothing");
    }

    [Fact]
    public async Task Pass_SourceUnavailable_AuditsTheFailedPairInTheHistoryLoadedEntry()
    {
        await AuthenticateAsync();
        var (eur, usd) = await CreateEurPrimaryAndUsdWalletsAsync(eurStartDaysAgo: 20, usdStartDaysAgo: 20);
        _fake.RatesFailure = FrankfurterFakeFailure.ServerError;

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var entries = (await Client.GetAuditEntriesAsync("ExchangeRateQuote", Guid.Empty)).Where(e => e.Action == "HistoryLoaded").ToList();
        var entry = entries.Should().ContainSingle().Subject;
        entry.Changes.Should().ContainSingle(c => c.Field == $"{usd}->{eur}" && !string.IsNullOrWhiteSpace(c.NewValue));
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// ADR-0004 §8: no GET touches the rate service. The counter of FrankfurterFake is reset after the preparation and must stay at zero
/// for every read, with and without loaded history, for the current total and a past one.
/// </summary>
public sealed class ReadPathDoesNotCallSourceTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task PrepareAsync(bool loadHistory)
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной", initialBalanceAmount: 10m, accountingStartDate: Today.AddDays(-60));
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD", initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-60));
        if (loadHistory)
        {
            await Fixture.RunExchangeRatesBackgroundPassAsync();
        }
        else
        {
            (await Client.PostAsync("/api/v1/exchange-rates/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _fake.ResetCounters();
    }

    private async Task AssertReadsDoNotCallTheServiceAsync()
    {
        string[] urls =
        [
            "/api/v1/reporting/total-amount",
            $"/api/v1/reporting/total-amount?date={Today.AddDays(-20):yyyy-MM-dd}",
            $"/api/v1/reporting/total-amount?date={Today.AddDays(-59):yyyy-MM-dd}",
            "/api/v1/exchange-rates/latest",
            "/api/v1/currencies",
            "/api/v1/currencies/available",
        ];

        foreach (var url in urls)
        {
            var response = await Client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            _fake.TotalCalls.Should().Be(0, $"GET {url} must read the database only (ADR-0004 §8)");
        }
    }

    [Fact]
    public async Task Reads_AfterTheHistoryWasLoaded_DoNotCallTheService()
    {
        await PrepareAsync(loadHistory: true);

        await AssertReadsDoNotCallTheServiceAsync();
    }

    [Fact]
    public async Task Reads_WithoutAnyLoadedHistory_DoNotCallTheServiceAndExcludeTheWalletWithAReason()
    {
        await PrepareAsync(loadHistory: false);

        await AssertReadsDoNotCallTheServiceAsync();

        var past = await Client.GetFromJsonAsync<TotalAmountResponse>($"/api/v1/reporting/total-amount?date={Today.AddDays(-20):yyyy-MM-dd}");
        past!.ExcludedWallets.Should().ContainSingle(w => !string.IsNullOrWhiteSpace(w.Reason), "no history is loaded yet: the USD wallet is excluded, not fetched");
        _fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Reads_OfAnEmptyApplication_DoNotCallTheService()
    {
        await AuthenticateAsync();

        foreach (var url in new[] { "/api/v1/exchange-rates/latest", "/api/v1/currencies", "/api/v1/currencies/available" })
        {
            (await Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _fake.TotalCalls.Should().Be(0);
    }
}
