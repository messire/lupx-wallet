using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Reporting;

// DTO для десериализации ответов Reporting API (docs/api/openapi.yaml, схема TotalAmount).
public sealed record ReportingMoneyResponse(string Amount, Guid CurrencyId);
public sealed record ExcludedWalletResponse(Guid WalletId, string Reason);
public sealed record TotalAmountResponse(
    DateOnly? Date, ReportingMoneyResponse Amount, DateOnly RatesAsOfDate, IReadOnlyList<ExcludedWalletResponse> ExcludedWallets);

/// <summary>
/// Reporting API (docs/api/openapi.yaml, тег Reporting) — GET /total-amount, текущая (UC-19)
/// и историческая (UC-21) сумма. Риск №6 (docs/PROGRESS.md, П.5.1): кошелек без курса для
/// своей валюты исключается из суммы с явной пометкой — основной путь, не 409, не курс 1:1.
/// HTTP-вызов к Frankfurter подменен через FrankfurterTestableHandler (см. ExchangeRatesApiTests).
/// </summary>
public sealed class ReportingApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterTestableHandler _frankfurterHandler = ResetOverride(fixture.Factory.GetFrankfurterTestableHandler());

    private static FrankfurterTestableHandler ResetOverride(FrankfurterTestableHandler handler)
    {
        handler.TestOverride = null;
        return handler;
    }

    private void SetSuccessfulRate(decimal rate)
    {
        _frankfurterHandler.TestOverride = (request, _) =>
        {
            var query = request.RequestUri!.Query.TrimStart('?');
            var symbols = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .First(p => Uri.UnescapeDataString(p[0]) == "symbols")[1];
            var json = "{\"amount\":1,\"base\":\"EUR\",\"date\":\"2024-01-01\",\"rates\":{\""
                + Uri.UnescapeDataString(symbols) + "\":" + rate.ToString(CultureInfo.InvariantCulture) + "}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        };
    }

    private async Task<TotalAmountResponse> GetTotalAmountAsync(DateOnly? date = null)
    {
        var url = date is { } d ? $"/api/v1/reporting/total-amount?date={d:yyyy-MM-dd}" : "/api/v1/reporting/total-amount";
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TotalAmountResponse>())!;
    }

    [Fact]
    public async Task GetTotalAmount_ThreeWalletsTwoCurrencies_ConvertsSecondaryAndSumsExactly()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var secondCurrency = await Client.CreateCurrencyAsync("USD", "Доллар США");

        // Первый созданный кошелек становится основным (Wallet.Create) — задает валюту отображения (EUR).
        await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: primaryCurrency.Id, name: "Основной");
        await Client.CreateWalletAsync(initialBalanceAmount: 50m, currencyId: primaryCurrency.Id, name: "Второй EUR");
        await Client.CreateWalletAsync(initialBalanceAmount: 30m, currencyId: secondCurrency.Id, name: "USD");

        SetSuccessfulRate(0.9m);
        var refreshResponse = await Client.PostAsync("/api/v1/exchange-rates/refresh", null);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await GetTotalAmountAsync();

        body.ExcludedWallets.Should().BeEmpty();
        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(100m + 50m + 30m * 0.9m);
        body.Amount.CurrencyId.Should().Be(primaryCurrency.Id);
        body.Date.Should().BeNull();
    }

    [Fact]
    public async Task GetTotalAmount_WalletCurrencyWithoutRate_IsExcludedWithReasonAndSumsRemainingWallets()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var unratedCurrency = await Client.CreateCurrencyAsync("XYZ", "Без курса");

        await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: primaryCurrency.Id, name: "Основной");
        var unratedWallet = await Client.CreateWalletAsync(initialBalanceAmount: 30m, currencyId: unratedCurrency.Id, name: "Без курса");

        // Курс для unratedCurrency ни разу не запрашивался/не обновлялся — GetRateAsync вернет null.
        var body = await GetTotalAmountAsync();

        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(100m, "кошелек без курса исключается из суммы (риск №6), а не блокирует ответ и не считается 1:1");
        body.ExcludedWallets.Should().ContainSingle(w => w.WalletId == unratedWallet.Id && !string.IsNullOrWhiteSpace(w.Reason));
    }

    [Fact]
    public async Task GetTotalAmount_DateBeforeAccountingStart_ReturnsZero()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        var accountingStart = DateOnly.FromDateTime(DateTime.UtcNow);
        await Client.CreateWalletAsync(
            initialBalanceAmount: 100m, currencyId: primaryCurrency.Id, accountingStartDate: accountingStart, name: "Основной");

        var body = await GetTotalAmountAsync(accountingStart.AddDays(-1));

        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(0m, "Q13: баланс раньше accounting_start_date всегда равен нулю");
        body.ExcludedWallets.Should().BeEmpty();
        body.Date.Should().Be(accountingStart.AddDays(-1));
    }
}
