using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.ExchangeRates;

// DTO для десериализации ответов ExchangeRates API (docs/api/openapi.yaml).
public sealed record ExchangeRateQuoteResponse(Guid FromCurrencyId, Guid ToCurrencyId, string Rate);
public sealed record LatestExchangeRatesResponse(DateTimeOffset? LastSuccessfulUpdate, IReadOnlyList<ExchangeRateQuoteResponse> Rates);

/// <summary>
/// ExchangeRates API (docs/api/openapi.yaml, тег ExchangeRates) — GET /latest,
/// POST /refresh, включая 502 при недоступности Frankfurter (ADR-0001 п.6) и пропуск
/// валюты, не поддерживаемой Frankfurter (ADR-0001, "Последствия"). HTTP-вызов к Frankfurter
/// подменён через FrankfurterTestableHandler — реального сетевого вызова нет
/// (docs/PROGRESS.md, W1.1 DoD).
/// </summary>
public sealed class ExchangeRatesApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    // xunit создаёт новый экземпляр класса теста на каждый [Fact] — сброс TestOverride здесь
    // (в инициализаторе поля primary-конструктора) гарантирует, что предыдущий тест не
    // оставит подмену ответа Frankfurter для следующего (Singleton на весь прогон коллекции).
    private readonly FrankfurterTestableHandler _frankfurterHandler = ResetOverride(fixture.Factory.GetFrankfurterTestableHandler());

    private static FrankfurterTestableHandler ResetOverride(FrankfurterTestableHandler handler)
    {
        handler.TestOverride = null;
        return handler;
    }

    private static string GetQueryParam(Uri uri, string name)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty);
            }
        }

        return string.Empty;
    }

    private void SetSuccessfulRate(decimal rate)
    {
        _frankfurterHandler.TestOverride = (request, _) =>
        {
            var symbols = GetQueryParam(request.RequestUri!, "symbols");
            var json = "{\"amount\":1,\"base\":\"EUR\",\"date\":\"2024-01-01\",\"rates\":{\""
                + symbols + "\":" + rate.ToString(CultureInfo.InvariantCulture) + "}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        };
    }

    private void SetUnavailable()
    {
        _frankfurterHandler.TestOverride = (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private void SetUnsupportedCurrency()
    {
        _frankfurterHandler.TestOverride = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            // Frankfurter отвечает 200, но без запрошенной валюты в "rates" — ровно так себя
            // ведёт реальный сервис для неподдерживаемого symbols (ADR-0001, "Последствия").
            Content = new StringContent("""{"amount":1,"base":"EUR","date":"2024-01-01","rates":{}}""", Encoding.UTF8, "application/json"),
        });
    }

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
    public async Task Refresh_SuccessfulFetch_UpdatesRateAndReflectsInLatest()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var secondCurrency = await Client.CreateCurrencyAsync("USD", "Доллар США");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной"); // первый кошелек -> primary
        await Client.CreateWalletAsync(currencyId: secondCurrency.Id, name: "Второй");
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
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var secondCurrency = await Client.CreateCurrencyAsync("BTC", "Биткоин");
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
        var eur = await Client.CreateCurrencyAsync("EUR", "Евро");
        var usd = await Client.CreateCurrencyAsync("USD", "Доллар США");
        var eurWallet = await Client.CreateWalletAsync(currencyId: eur.Id, name: "Евро (основной)"); // первый -> primary
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "Доллары");
        SetSuccessfulRate(0.92m);
        var firstRefresh = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await firstRefresh.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        firstBody!.Rates.Should().ContainSingle(r => r.ToCurrencyId == eur.Id);

        // Меняем основной кошелек на GBP — фоновый пересчёт курсов (ADR-0011) в тестах не
        // запускается (IHostedService отключены, LupexWalletApiFactory), поэтому это ровно то
        // окно "смена основного кошелька уже произошла, обновление курсов ещё нет", которое
        // и должен закрывать читающий фильтр в GetLatestExchangeRatesQueryHandler.
        var gbp = await Client.CreateCurrencyAsync("GBP", "Фунт стерлингов");
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
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var secondCurrency = await Client.CreateCurrencyAsync("USD", "Доллар США");
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
        secondBody.Rates.Should().ContainSingle(r => decimal.Parse(r.Rate, CultureInfo.InvariantCulture) == 0.9m, "прежний курс сохраняется при недоступности источника (ADR-0001 п.6)");
    }

    [Fact]
    public async Task Refresh_CurrencyUnsupportedByFrankfurter_SkipsPairAndStillReturnsOk()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var unsupportedCurrency = await Client.CreateCurrencyAsync("XYZ", "Неизвестная валюта");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: unsupportedCurrency.Id, name: "Второй");
        SetUnsupportedCurrency();

        var response = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "валюта, не поддерживаемая Frankfurter, пропускается молча, не эскалируется до 502");
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().BeEmpty();
    }
}
