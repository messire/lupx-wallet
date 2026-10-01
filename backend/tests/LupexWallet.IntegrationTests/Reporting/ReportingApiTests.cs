using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
/// и историческая (UC-21) сумма. Кошелек без курса для своей валюты исключается из суммы
/// с явной пометкой (ADR-0004 §9) — основной путь, не 409, не курс 1:1.
/// HTTP-вызов к Frankfurter подменен через FrankfurterFake; чтение суммы к сервису не обращается (ADR-0004 §8).
/// </summary>
public sealed class ReportingApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private void SetSuccessfulRate(decimal rate) => _fake.DefaultRate = rate;

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
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var secondCurrency = await Client.CreateCurrencyAsync("USD");

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
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var unratedCurrency = await Client.CreateCurrencyAsync("XYZ");

        await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: primaryCurrency.Id, name: "Основной");
        var unratedWallet = await Client.CreateWalletAsync(initialBalanceAmount: 30m, currencyId: unratedCurrency.Id, name: "Без курса");

        // Курс для unratedCurrency ни разу не запрашивался/не обновлялся — GetRateAsync вернет null.
        var body = await GetTotalAmountAsync();

        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(100m, "кошелек без курса исключается из суммы (риск №6), а не блокирует ответ и не считается 1:1");
        body.ExcludedWallets.Should().ContainSingle(w => w.WalletId == unratedWallet.Id && w.Reason == "Курс валюты XYZ недоступен.");
    }

    [Fact]
    public async Task GetTotalAmount_CurrentAndHistorical_ExcludeDeletedOperationAndCountItAgainAfterRestore()
    {
        // ADR-0002, §3: a soft-deleted operation is excluded from the current total (Wallet.CurrentBalance)
        // and from the historical one (balance on date), and comes back on restore.
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wallet = await Client.CreateWalletAsync(
            initialBalanceAmount: 100m, currencyId: currency.Id, accountingStartDate: today.AddDays(-10), name: "Основной");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationDate = today.AddDays(-3);
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 50m, operationDate);
        decimal Parse(TotalAmountResponse r) => decimal.Parse(r.Amount.Amount, CultureInfo.InvariantCulture);
        Parse(await GetTotalAmountAsync()).Should().Be(150m);
        Parse(await GetTotalAmountAsync(operationDate)).Should().Be(150m);

        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Parse(await GetTotalAmountAsync()).Should().Be(100m);
        Parse(await GetTotalAmountAsync(operationDate)).Should().Be(100m);
        Parse(await GetTotalAmountAsync(today)).Should().Be(100m);

        (await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        Parse(await GetTotalAmountAsync()).Should().Be(150m);
        Parse(await GetTotalAmountAsync(operationDate)).Should().Be(150m);
        Parse(await GetTotalAmountAsync(operationDate.AddDays(-1))).Should().Be(100m, "before the operation date the balance never included it");
    }

    [Fact]
    public async Task GetTotalAmount_DeletedTransfer_NoLongerReducesTheIncludedSourceWallet()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR");
        var walletType = await Client.CreateWalletTypeAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var source = await Client.CreateWalletAsync(500m, today.AddDays(-10), "Основной", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(100m, today.AddDays(-10), "Второй", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        // The target wallet is left out of the total, so the transfer visibly changes the sum.
        var excludeResponse = await Client.PatchAsJsonAsync($"/api/v1/wallets/{target.Id}", new
        {
            Name = target.Name,
            target.WalletTypeId,
            PurposeDescription = (string?)null,
            IncludeInTotal = false,
            DisplayOrder = 0,
            Color = (string?)null,
            Icon = (string?)null,
        });
        excludeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 200m,
            TransferDate = today.AddDays(-2),
        });
        transferResponse.EnsureSuccessStatusCode();
        var transferId = (await transferResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        decimal Parse(TotalAmountResponse r) => decimal.Parse(r.Amount.Amount, CultureInfo.InvariantCulture);
        Parse(await GetTotalAmountAsync()).Should().Be(300m);
        Parse(await GetTotalAmountAsync(today.AddDays(-2))).Should().Be(300m);

        await Client.DeleteAsync($"/api/v1/transfers/{transferId}");

        Parse(await GetTotalAmountAsync()).Should().Be(500m);
        Parse(await GetTotalAmountAsync(today.AddDays(-2))).Should().Be(500m);
        Parse(await GetTotalAmountAsync(today.AddDays(-3))).Should().Be(500m);

        await Client.PostAsync($"/api/v1/transfers/{transferId}/restore", null);

        Parse(await GetTotalAmountAsync()).Should().Be(300m);
        Parse(await GetTotalAmountAsync(today.AddDays(-2))).Should().Be(300m);
        Parse(await GetTotalAmountAsync(today.AddDays(-3))).Should().Be(500m);
    }

    [Fact]
    public async Task GetTotalAmount_DateBeforeAccountingStart_ReturnsZero()
    {
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("EUR");
        var accountingStart = DateOnly.FromDateTime(DateTime.UtcNow);
        await Client.CreateWalletAsync(
            initialBalanceAmount: 100m, currencyId: primaryCurrency.Id, accountingStartDate: accountingStart, name: "Основной");

        var body = await GetTotalAmountAsync(accountingStart.AddDays(-1));

        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(0m, "Q13: баланс раньше accounting_start_date всегда равен нулю");
        body.ExcludedWallets.Should().BeEmpty();
        body.Date.Should().Be(accountingStart.AddDays(-1));
    }

    [Fact]
    public async Task GetTotalAmount_HistoricalDate_UsesLoadedHistoryAndClosestEarlierRateWithoutCallingSource()
    {
        await AuthenticateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(initialBalanceAmount: 0m, currencyId: eur.Id, name: "Основной", accountingStartDate: today.AddDays(-60));
        await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: usd.Id, name: "USD", accountingStartDate: today.AddDays(-60));
        // The rate differs per day, so the test sees which day was applied.
        _fake.RateForDate = (_, _, date) => 1m + (date.DayNumber % 100) / 1000m;
        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var saturday = today.AddDays(-30);
        while (saturday.DayOfWeek != DayOfWeek.Saturday)
        {
            saturday = saturday.AddDays(-1);
        }

        var friday = saturday.AddDays(-1);
        _fake.ResetCounters();

        var body = await GetTotalAmountAsync(saturday);

        body.ExcludedWallets.Should().BeEmpty();
        body.RatesAsOfDate.Should().Be(friday, "у субботы курса нет — применяется ближайший более ранний");
        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(100m * (1m + (friday.DayNumber % 100) / 1000m));
        _fake.TotalCalls.Should().Be(0, "чтение сумм не обращается к Frankfurter (ADR-0004 §8)");
    }

    [Fact]
    public async Task GetTotalAmount_HistoricalDateWithoutLoadedHistory_ExcludesWalletWithReasonAndDoesNotCallSource()
    {
        await AuthenticateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(initialBalanceAmount: 0m, currencyId: eur.Id, name: "Основной", accountingStartDate: today.AddDays(-60));
        var usdWallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: usd.Id, name: "USD", accountingStartDate: today.AddDays(-60));
        _fake.ResetCounters();

        var body = await GetTotalAmountAsync(today.AddDays(-10));

        decimal.Parse(body.Amount.Amount, CultureInfo.InvariantCulture).Should().Be(0m);
        body.ExcludedWallets.Should().ContainSingle(
            w => w.WalletId == usdWallet.Id && w.Reason == $"Курс валюты USD на дату {today.AddDays(-10):yyyy-MM-dd} недоступен.");
        _fake.TotalCalls.Should().Be(0, "дозагрузка истории — работа фонового сервиса, не чтения (ADR-0004 §8, §9)");
    }

    [Fact]
    public async Task GetTotalAmount_WithStatedBalance_CurrentAndHistoricalTotalsFollowTheStatedBalance()
    {
        // ADR-0003, §6: the correction is part of the wallet balance: the total of the stated day is the stated one,
        // the current total is the CurrentBalance (initial + all effects, corrections included).
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wallet = await Client.CreateWalletAsync(
            initialBalanceAmount: 100m, currencyId: currency.Id, accountingStartDate: today.AddDays(-10), name: "Основной");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationDate = today.AddDays(-6);
        var statedDate = today.AddDays(-3);
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 50m, operationDate);
        decimal Parse(TotalAmountResponse r) => decimal.Parse(r.Amount.Amount, CultureInfo.InvariantCulture);
        Parse(await GetTotalAmountAsync()).Should().Be(150m);

        await Client.SetStatedBalanceAsync(wallet.Id, statedDate, 130m); // correction -20

        Parse(await GetTotalAmountAsync()).Should().Be(130m, "the current total includes the correction");
        Parse(await GetTotalAmountAsync(statedDate)).Should().Be(130m);
        Parse(await GetTotalAmountAsync(today)).Should().Be(130m);
        Parse(await GetTotalAmountAsync(statedDate.AddDays(-1))).Should().Be(150m, "before the stated date the correction does not apply");
        Parse(await GetTotalAmountAsync(operationDate.AddDays(-1))).Should().Be(100m);

        // A later operation is not absorbed: it moves the current total again.
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 10m, today.AddDays(-1));
        Parse(await GetTotalAmountAsync()).Should().Be(140m);
        Parse(await GetTotalAmountAsync(statedDate)).Should().Be(130m);
    }
}
