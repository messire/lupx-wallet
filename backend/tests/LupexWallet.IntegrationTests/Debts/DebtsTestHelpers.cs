using System.Net.Http.Json;
using LupexWallet.IntegrationTests.Infrastructure;

namespace LupexWallet.IntegrationTests.Debts;

public sealed record CreditorResponse(Guid Id, string Name, bool IsActive);
public sealed record CreditorPage(List<CreditorResponse> Data, PageMeta Pagination);
public sealed record PageMeta(string? NextCursor, bool HasMore);

public sealed record PaymentResponse(
    Guid Id, Guid OperationId, Guid WalletId, MoneyResponse Amount, DateOnly PaymentDate, bool IsCounted, bool IsOperationDeleted);

public sealed record DebtResponse(
    Guid Id, Guid CreditorId, string Name, Guid CurrencyId, MoneyResponse OriginalAmount, MoneyResponse RepaidAmount,
    MoneyResponse RemainingAmount, DateOnly StartDate, string Status, DateTimeOffset? CancelledAt, List<PaymentResponse> Repayments);

public sealed record DebtPage(List<DebtResponse> Data, PageMeta Pagination);

public sealed record DebtCreditorSummary(Guid CreditorId, string CreditorName, MoneyResponse RemainingAmount, int DebtCount);
public sealed record DebtCurrencySummary(Guid CurrencyId, MoneyResponse TotalRemainingAmount, int DebtCount, List<DebtCreditorSummary> Creditors);
public sealed record DebtSummary(List<DebtCurrencySummary> Currencies);

public sealed record LoanScheduleItemResponse(DateOnly DueDate, MoneyResponse Amount, MoneyResponse PaidAmount, MoneyResponse OutstandingAmount, string Status);

public sealed record LoanResponse(
    Guid Id, Guid CreditorId, string Name, Guid CurrencyId, MoneyResponse AmountToRepay, MoneyResponse PaidAmount,
    MoneyResponse RemainingAmount, MoneyResponse ScheduledAmount, DateOnly StartDate, string Status,
    LoanScheduleItemResponse? NextPayment, List<LoanScheduleItemResponse> Schedule, List<PaymentResponse> Payments);

public sealed record LoanPage(List<LoanResponse> Data, PageMeta Pagination);

/// <summary>Arrange helpers for Debts integration tests.</summary>
public static class DebtsTestHelpers
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public static async Task<CreditorResponse> CreateCreditorAsync(this HttpClient client, string name = "Банк")
    {
        var response = await client.PostAsJsonAsync("/api/v1/creditors", new { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreditorResponse>())!;
    }

    public static async Task<DebtResponse> CreateDebtAsync(
        this HttpClient client, Guid creditorId, Guid currencyId, decimal amount, string name = "Долг")
    {
        var response = await client.PostAsJsonAsync("/api/v1/debts", new
        {
            CreditorId = creditorId, Name = name, CurrencyId = currencyId, OriginalAmount = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartDate = Today.AddMonths(-1),
        });
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<DebtResponse>())!;
    }

    public static async Task<LoanResponse> CreateLoanAsync(this HttpClient client, Guid creditorId, Guid currencyId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/v1/loans", new
        {
            CreditorId = creditorId, Name = "Кредит", CurrencyId = currencyId, AmountToRepay = amount, StartDate = Today.AddMonths(-3),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoanResponse>())!;
    }

    public static Task<HttpResponseMessage> PayAsync(
        this HttpClient client, string url, Guid walletId, Guid operationTypeId, decimal amount, DateOnly? date = null) =>
        client.PostAsJsonAsync(url, new
        {
            WalletId = walletId, OperationTypeId = operationTypeId, Amount = amount, PaymentDate = date ?? Today,
        });

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public static decimal Value(this MoneyResponse money) =>
        decimal.Parse(money.Amount, System.Globalization.CultureInfo.InvariantCulture);
}
