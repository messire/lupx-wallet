using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Domain;

// Domain events of the Debts module (ADR-0010 §3, §8). Audit subscribes to all of them.
// Repaid/overdue states are derived from operation facts on read, so they have no events.

public sealed record CreditorCreated(CreditorId CreditorId) : DomainEvent;

public sealed record CreditorRenamed(CreditorId CreditorId) : DomainEvent;

public sealed record CreditorDeactivated(CreditorId CreditorId) : DomainEvent;

public sealed record CreditorDeleted(CreditorId CreditorId) : DomainEvent;

public sealed record DebtCreated(DebtId DebtId) : DomainEvent;

public sealed record DebtUpdated(DebtId DebtId) : DomainEvent;

public sealed record DebtCancelled(DebtId DebtId) : DomainEvent;

public sealed record DebtDeleted(DebtId DebtId) : DomainEvent;

/// <summary>A repayment expense was recorded and linked; Funds releases reserves on it (ADR-0010 §4, B9).</summary>
public sealed record DebtRepaymentRecorded(DebtId DebtId, WalletId WalletId, OperationId OperationId, Money Amount) : DomainEvent
{
    public DateOnly RepaymentDate { get; init; }
}

public sealed record LoanCreated(LoanId LoanId) : DomainEvent;

public sealed record LoanUpdated(LoanId LoanId) : DomainEvent;

public sealed record LoanCancelled(LoanId LoanId) : DomainEvent;

public sealed record LoanDeleted(LoanId LoanId) : DomainEvent;

/// <summary>The schedule was replaced (edited or generated); both versions are carried for the audit diff.</summary>
public sealed record LoanScheduleChanged(
    LoanId LoanId, IReadOnlyList<LoanScheduleEntry> PreviousSchedule, IReadOnlyList<LoanScheduleEntry> NewSchedule) : DomainEvent;

/// <summary>A loan payment expense was recorded and linked; Funds releases reserves on it (ADR-0010 §4, B9).</summary>
public sealed record LoanPaymentRecorded(LoanId LoanId, WalletId WalletId, OperationId OperationId, Money Amount) : DomainEvent
{
    public DateOnly PaymentDate { get; init; }
}
