using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Application;

public sealed record CreditorDto(Guid Id, string Name, bool IsActive)
{
    public static CreditorDto FromDomain(Creditor creditor) => new(creditor.Id.Value, creditor.Name, creditor.IsActive);
}

public sealed record CreditorPageDto(IReadOnlyList<CreditorDto> Data, string? NextCursor, bool HasMore);

/// <summary>
/// A linked repayment/payment. Amount and date are taken from the operation as it is now (it may have
/// been edited); the values recorded at linking are used only if the operation is gone.
/// </summary>
public sealed record PaymentDto(
    Guid Id, Guid OperationId, Guid WalletId, decimal Amount, DateOnly PaymentDate,
    bool IsCounted, bool IsOperationDeleted, DateTimeOffset CreatedAt);

public sealed record DebtDto(
    Guid Id,
    Guid CreditorId,
    string Name,
    Guid CurrencyId,
    decimal OriginalAmount,
    decimal RepaidAmount,
    decimal RemainingAmount,
    DateOnly StartDate,
    string Status,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PaymentDto> Repayments);

public sealed record DebtPageDto(IReadOnlyList<DebtDto> Data, string? NextCursor, bool HasMore);

public sealed record LoanScheduleItemDto(DateOnly DueDate, decimal Amount, decimal PaidAmount, decimal OutstandingAmount, string Status);

public sealed record LoanDto(
    Guid Id,
    Guid CreditorId,
    string Name,
    Guid CurrencyId,
    decimal AmountToRepay,
    decimal PaidAmount,
    decimal RemainingAmount,
    decimal ScheduledAmount,
    DateOnly StartDate,
    string Status,
    LoanScheduleItemDto? NextPayment,
    IReadOnlyList<LoanScheduleItemDto> Schedule,
    IReadOnlyList<PaymentDto> Payments,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LoanPageDto(IReadOnlyList<LoanDto> Data, string? NextCursor, bool HasMore);

public sealed record DebtCreditorSummaryDto(Guid CreditorId, string CreditorName, decimal RemainingAmount, int DebtCount);

public sealed record DebtCurrencySummaryDto(Guid CurrencyId, decimal TotalRemainingAmount, int DebtCount, IReadOnlyList<DebtCreditorSummaryDto> Creditors);

/// <summary>Grouped by currency; conversion to the primary currency is Reporting's job (ADR-0010 §7).</summary>
public sealed record DebtSummaryDto(IReadOnlyList<DebtCurrencySummaryDto> Currencies);

/// <summary>Builds DTOs from aggregates and the current operation facts.</summary>
public static class DebtsDtoMapper
{
    public static DebtDto ToDto(Debt debt, PaymentFacts facts)
    {
        var repaid = facts.CountedTotal(debt.Repayments.Select(x => x.OperationId), debt.CurrencyId);
        return new DebtDto(
            debt.Id.Value, debt.CreditorId.Value, debt.Name, debt.CurrencyId.Value,
            debt.OriginalAmount.Amount, repaid, debt.RemainingAmount(repaid).Amount, debt.StartDate,
            debt.Status(repaid).ToString(), debt.CancelledAt, debt.CreatedAt, debt.UpdatedAt,
            debt.Repayments
                .Select(x => ToPayment(x.Id, x.OperationId, x.WalletId, x.Amount, x.RepaymentDate, x.CreatedAt, debt.CurrencyId, facts))
                .OrderBy(x => x.PaymentDate).ThenBy(x => x.CreatedAt)
                .ToList());
    }

    public static LoanDto ToDto(Loan loan, PaymentFacts facts, DateOnly today)
    {
        var paid = facts.CountedTotal(loan.Payments.Select(x => x.OperationId), loan.CurrencyId);
        var states = loan.EvaluateSchedule(paid, today);
        var next = LoanScheduleCalculator.NextPayment(states);
        return new LoanDto(
            loan.Id.Value, loan.CreditorId.Value, loan.Name, loan.CurrencyId.Value,
            loan.AmountToRepay.Amount, paid, loan.RemainingAmount(paid).Amount, states.Sum(x => x.Amount),
            loan.StartDate, loan.Status(paid).ToString(),
            loan.IsCancelled || next is null ? null : ToItem(next),
            states.Select(ToItem).ToList(),
            loan.Payments
                .Select(x => ToPayment(x.Id, x.OperationId, x.WalletId, x.Amount, x.PaymentDate, x.CreatedAt, loan.CurrencyId, facts))
                .OrderBy(x => x.PaymentDate).ThenBy(x => x.CreatedAt)
                .ToList(),
            loan.CancelledAt, loan.CreatedAt, loan.UpdatedAt);
    }

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static LoanScheduleItemDto ToItem(LoanScheduleItemState state) =>
        new(state.DueDate, state.Amount, state.PaidAmount, state.OutstandingAmount, state.Status.ToString());

    private static PaymentDto ToPayment(
        Guid id, OperationId operationId, WalletId walletId, decimal recordedAmount, DateOnly recordedDate,
        DateTimeOffset createdAt, CurrencyId currencyId, PaymentFacts facts)
    {
        var fact = facts.Find(operationId);
        return new PaymentDto(
            id, operationId.Value, (fact?.WalletId ?? walletId).Value, fact?.Amount ?? recordedAmount, fact?.OperationDate ?? recordedDate,
            facts.IsCounted(operationId, currencyId), fact?.IsDeleted ?? true, createdAt);
    }
}
