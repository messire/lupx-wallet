using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Application;

/// <summary>
/// Read-only port published by Debts (ADR-0001 §2, way c: owned by the data owner, implemented in
/// Debts.Infrastructure). Consumers: Funds (link validation, B9 payment lookup) and Reporting
/// (debts summary, upcoming payments, cash-flow forecast — ADR-0010 §7). Paid amounts are derived
/// from the current operation facts, the same way the Debts API does.
/// </summary>
public interface IDebtObligationsLookup
{
    /// <summary>The debt, or null if it does not exist.</summary>
    Task<ObligationReference?> FindDebtAsync(Guid debtId, CancellationToken cancellationToken);

    /// <summary>The loan, or null if it does not exist.</summary>
    Task<ObligationReference?> FindLoanAsync(Guid loanId, CancellationToken cancellationToken);

    /// <summary>The debt repayment or loan payment linked to the operation, or null if the operation pays nothing.</summary>
    Task<LinkedObligationPayment?> FindPaymentByOperationAsync(OperationId operationId, CancellationToken cancellationToken);

    /// <summary>Not cancelled debts and loans with a remaining amount above zero.</summary>
    Task<IReadOnlyList<OpenObligation>> ListOpenObligationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Not fully paid schedule items of open loans with DueDate ≤ <paramref name="to"/>, overdue
    /// ones included (no lower bound), ordered by due date.
    /// </summary>
    Task<IReadOnlyList<UnpaidLoanScheduleItem>> ListUnpaidScheduleItemsAsync(DateOnly to, CancellationToken cancellationToken);
}

public enum ObligationKind
{
    Debt,
    Loan,
}

public sealed record ObligationReference(ObligationKind Kind, Guid Id, CurrencyId CurrencyId, bool IsCancelled);

/// <param name="Amount">Amount recorded when the payment was linked, in the obligation currency.</param>
public sealed record LinkedObligationPayment(ObligationKind Kind, Guid ObligationId, WalletId WalletId, decimal Amount);

public sealed record OpenObligation(
    ObligationKind Kind, Guid Id, string Name, Guid CreditorId, string CreditorName, CurrencyId CurrencyId, decimal RemainingAmount);

/// <param name="Status">Never Paid: one of Partial, Overdue, Expected.</param>
public sealed record UnpaidLoanScheduleItem(
    Guid LoanId,
    string LoanName,
    Guid CreditorId,
    string CreditorName,
    CurrencyId CurrencyId,
    DateOnly DueDate,
    decimal Amount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string Status,
    bool IsOverdue);
