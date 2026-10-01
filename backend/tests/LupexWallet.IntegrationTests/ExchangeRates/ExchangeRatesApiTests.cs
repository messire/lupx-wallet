using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.ExchangeRates;

// DTO для десериализации ответов ExchangeRates API (docs/api/openapi.yaml).
public sealed record ExchangeRateQuoteResponse(Guid FromCurrencyId, Guid ToCurrencyId, string Rate);
public sealed record LatestExchangeRatesResponse(DateTimeOffset? LastSuccessfulUpdate, IReadOnlyList<ExchangeRateQuoteResponse> Rates);

/// <summary>
/// ExchangeRates API (docs/api/openapi.yaml, тег ExchangeRates) — GET /latest,
/// POST /refresh, включая 502 при недоступности Frankfurter (ADR-0004 п.6) и пропуск
/// валюты, не поддерживаемой Frankfurter (ADR-0004, "Последствия"). HTTP-вызов к Frankfurter
/// подменён через FrankfurterFake — реального сетевого вызова нет.
/// </summary>
public sealed class ExchangeRatesApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    // xunit создаёт новый экземпляр класса теста на каждый [Fact] — новый FrankfurterFake заменяет
    // TestOverride Singleton-обработчика (общий на весь прогон коллекции) и сбрасывает настройки,
    // так что подмена предыдущего теста не доживает до следующего.
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private void SetSuccessfulRate(decimal rate) => _fake.DefaultRate = rate;

    private void SetUnavailable() => _fake.RatesFailure = FrankfurterFakeFailure.ServerError;

    // Frankfurter API v2 отвечает 200 и пустым массивом — ровно так себя ведёт реальный сервис для
    // валюты, которой у него нет (ADR-0004, §6).
    private void SetUnsupportedCurrency() => _fake.RatesFailure = FrankfurterFakeFailure.EmptyArray;

    [Fact]
    public async Task GetLatest_BeforeAnyRefresh_ReturnsNullUpdateAndEmptyRates()
    {
        await AuthenticateAsync();

        var response = await Client.GetAsync("/api/v1/exchange-rates/latest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.LastSuccessfulUpdate.Should().BeNull();
        body.Rates.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_NoWallets_ReturnsOkWithEmptyRates()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_ActiveCurrencyWithoutWallet_UpdatesRateAndReflectsInLatest()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var secondCurrency = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной"); // первый кошелек -> primary
        SetSuccessfulRate(0.92m);

        var refreshResponse = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        refreshBody!.LastSuccessfulUpdate.Should().NotBeNull();
        refreshBody.Rates.Should().ContainSingle(r =>
            r.FromCurrencyId == secondCurrency.Id && r.ToCurrencyId == primaryCurrency.Id
            && decimal.Parse(r.Rate, CultureInfo.InvariantCulture) == 0.92m);

        var latestResponse = await Client.GetAsync("/api/v1/exchange-rates/latest");
        var latestBody = await latestResponse.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        latestBody!.Rates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Refresh_VerySmallRate_FormatsAsPlainDecimalWithoutScientificNotation()
    {
        // Регресс: decimal.ToString("G29", InvariantCulture) переключается на научную нотацию
        // для значений < 0.0001 (например, "1E-05"), что нарушает контракт
        // docs/api/openapi.yaml (ExchangeRateQuoteQuote.rate: string/decimal) и не пройдёт
        // AMOUNT_PATTERN на фронтенде (frontend/src/app/core/money/money.ts).
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var secondCurrency = await Client.CreateCurrencyAsync("BTC");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: secondCurrency.Id, name: "Крипто");
        SetSuccessfulRate(0.00001m);

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().ContainSingle(r => r.Rate == "0.00001", "научная нотация недопустима в контракте Money/ExchangeRateQuote");
    }

    [Fact]
    public async Task GetLatest_AfterPrimaryWalletChanged_DoesNotReturnRatesToPreviousPrimaryCurrency()
    {
        // Регресс (M5): openapi.yaml — GET /exchange-rates/latest отдаёт курсы к ТЕКУЩЕЙ
        // основной валюте. До фикса эндпоинт отдавал все строки latest_exchange_rates без
        // фильтра, включая пары к уже неактуальной прежней основной валюте.
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        var eurWallet = await Client.CreateWalletAsync(currencyId: eur.Id, name: "Евро (основной)"); // первый -> primary
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "Доллары");
        SetSuccessfulRate(0.92m);
        var firstRefresh = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        firstBody!.Rates.Should().ContainSingle(r => r.ToCurrencyId == eur.Id);

        // Меняем основной кошелек на GBP — фоновый пересчёт курсов (ADR-0004) в тестах не
        // запускается (IHostedService отключены, LupexWalletApiFactory), поэтому это ровно то
        // окно "смена основного кошелька уже произошла, обновление курсов ещё нет", которое
        // и должен закрывать читающий фильтр в GetLatestExchangeRatesQueryHandler.
        var gbp = await Client.CreateCurrencyAsync("GBP");
        var gbpWallet = await Client.CreateWalletAsync(currencyId: gbp.Id, name: "Фунты");
        var setPrimaryResponse = await Client.PostAsync($"/api/v1/wallets/{gbpWallet.Id}/set-primary", null);
        setPrimaryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var latestResponse = await Client.GetAsync("/api/v1/exchange-rates/latest");
        var latestBody = await latestResponse.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();

        latestBody!.Rates.Should().BeEmpty("прежний курс USD->EUR не относится к новой основной валюте (GBP) и не должен отдаваться");

        // Ручной POST /refresh (без ожидания фонового сервиса) обновляет курсы к новой основной
        // валюте и физически удаляет устаревшую пару к прежней (не только скрывает фильтром).
        eurWallet.Should().NotBeNull();
        SetSuccessfulRate(0.85m);
        var secondRefresh = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);
        secondRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await secondRefresh.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        secondBody!.Rates.Should().OnlyContain(r => r.ToCurrencyId == gbp.Id, "устаревшие пары к прежней основной валюте должны быть удалены при обновлении");
    }

    [Fact]
    public async Task Refresh_FrankfurterUnavailable_ReturnsBadGatewayAndKeepsPreviousRate()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var secondCurrency = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: secondCurrency.Id, name: "Второй");

        SetSuccessfulRate(0.9m);
        var firstRefresh = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();

        SetUnavailable();
        var secondRefresh = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        secondRefresh.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var secondBody = await secondRefresh.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        secondBody!.LastSuccessfulUpdate.Should().Be(firstBody!.LastSuccessfulUpdate, "фиксированное требование раздела 2: время последнего успеха не меняется при ошибке");
        secondBody.Rates.Should().ContainSingle(r => decimal.Parse(r.Rate, CultureInfo.InvariantCulture) == 0.9m, "прежний курс сохраняется при недоступности источника (ADR-0004 п.6)");
    }

    [Fact]
    public async Task Refresh_CurrencyUnsupportedByFrankfurter_SkipsPairAndStillReturnsOk()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var unsupportedCurrency = await Client.CreateCurrencyAsync("XYZ");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: unsupportedCurrency.Id, name: "Второй");
        SetUnsupportedCurrency();

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "валюта, не поддерживаемая Frankfurter, пропускается молча, не эскалируется до 502");
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_CurrencyListUnavailable_ReturnsBadGatewayEvenWithoutPrimaryWallet()
    {
        await AuthenticateAsync();
        _fake.CurrenciesFailure = FrankfurterFakeFailure.ServerError;

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "сбой загрузки списка доступных валют — это сбой обновления (ADR-0004 §7)");
        _fake.CurrenciesCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_EmptyCurrencyList_ReturnsBadGateway()
    {
        await AuthenticateAsync();
        _fake.SetCatalog();

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway, "пустой список считается сбоем и не стирает сохранённый");
    }

    [Fact]
    public async Task BackgroundPass_LoadsHistoryInChunksAndRepeatedPassRequestsNothingNew()
    {
        await AuthenticateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной", accountingStartDate: today.AddDays(-400));
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD", accountingStartDate: today.AddDays(-300));

        var (_, backfill) = await Fixture.RunExchangeRatesBackgroundPassAsync();

        // Период [старт − 7; сегодня] = 408 дней, порции по 366: две загрузки, от новых дат к старым.
        var periodRequests = _fake.RatesRequests.Where(r => r.From is not null).ToList();
        periodRequests.Should().HaveCount(2);
        periodRequests[0].To.Should().Be(today);
        periodRequests[1].From.Should().Be(today.AddDays(-407));
        backfill.Loaded.Should().ContainSingle(l => l.RangeFrom == today.AddDays(-407) && l.RangeTo == today);

        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExchangeRatesDbContext>();
            var coverage = await db.HistoricalRateCoverages.AsNoTracking().SingleAsync();
            coverage.CoveredFrom.Should().Be(today.AddDays(-407));
            coverage.CoveredTo.Should().Be(today);
            (await db.HistoricalExchangeRates.CountAsync()).Should().BeGreaterThan(250, "будни за 408 дней");
        }

        _fake.ResetCounters();
        await Fixture.RunExchangeRatesBackgroundPassAsync();

        _fake.RatesRequests.Where(r => r.From is not null).Should().BeEmpty("покрытие уже доходит до сегодняшнего дня и до левой границы");
    }
}
