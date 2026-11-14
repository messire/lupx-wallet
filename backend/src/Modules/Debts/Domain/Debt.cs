using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Domain;

/// <summary>
/// Debt to a creditor (ADR-0010 §3, issue #6). Holds no money: a repayment is a regular expense
/// operation, the debt keeps only the link (<see cref="DebtRepayment"/>). Which repayments count
/// is decided by the caller from operation facts (a soft-deleted operation stops counting), so
/// the remaining amount and the Repaid status are computed, never stored.
/// </summary>
public sealed class Debt : AggregateRoot<DebtId>
{
    private readonly List<DebtRepayment> _repayments = [];
    private decimal _originalAmount;

    public CreditorId CreditorId { get; private set; }
    public string Name { get; private set; } = null!;
    public CurrencyId CurrencyId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Money OriginalAmount => new(_originalAmount, CurrencyId);
    public bool IsCancelled => CancelledAt is not null;
    public IReadOnlyList<DebtRepayment> Repayments => _repayments.AsReadOnly();

    private Debt()
    {
        // EF Core only.
    }

    public static Debt Create(DebtId id, CreditorId creditorId, string name, Money originalAmount, DateOnly startDate)
    {
        EnsurePositive(originalAmount.Amount);
        var now = DateTimeOffset.UtcNow;
        var debt = new Debt
        {
            Id = id,
            CreditorId = creditorId,
            Name = Creditor.RequireName(name),
            CurrencyId = originalAmount.CurrencyId,
            _originalAmount = originalAmount.Amount,
            StartDate = startDate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        debt.Raise(new DebtCreated(id));
        return debt;
    }

    /// <summary>Currency is fixed at creation: repayments are linked in that currency.</summary>
    public void Update(CreditorId creditorId, string name, decimal originalAmount, DateOnly startDate)
    {
        EnsureActive();
        EnsurePositive(originalAmount);

        CreditorId = creditorId;
        Name = Creditor.RequireName(name);
        _originalAmount = originalAmount;
        StartDate = startDate;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new DebtUpdated(Id));
    }

    /// <summary>Original amount minus counted repayments; never below zero.</summary>
    public Money RemainingAmount(decimal countedRepaid) => new(Math.Max(0m, _originalAmount - countedRepaid), CurrencyId);

    public ObligationStatus Status(decimal countedRepaid) =>
        IsCancelled ? ObligationStatus.Cancelled
        : _originalAmount - countedRepaid <= 0m ? ObligationStatus.Repaid
        : ObligationStatus.Active;

    /// <summary>
    /// Links an already recorded expense operation. <paramref name="countedRepaid"/> is the sum
    /// of the repayments that currently count (their operations are not deleted).
    /// </summary>
    public DebtRepayment RecordRepayment(
        OperationId operationId, WalletId walletId, Money amount, DateOnly repaymentDate, decimal countedRepaid)
    {
        EnsureCanAcceptRepayment(amount, countedRepaid);

        var repayment = DebtRepayment.Create(operationId, walletId, amount.Amount, repaymentDate);
        _repayments.Add(repayment);
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new DebtRepaymentRecorded(Id, walletId, operationId, amount) { RepaymentDate = repaymentDate });
        return repayment;
    }

    /// <summary>Validation shared by the command (before the operation is created) and <see cref="RecordRepayment"/>.</summary>
    public void EnsureCanAcceptRepayment(Money amount, decimal countedRepaid)
    {
        EnsureActive();
        if (amount.CurrencyId != CurrencyId)
        {
            throw new PaymentCurrencyMismatchException();
        }

        EnsurePositive(amount.Amount);
        var remaining = RemainingAmount(countedRepaid).Amount;
        if (remaining <= 0m)
        {
            throw new NothingToRepayException();
        }

        if (amount.Amount > remaining)
        {
            throw new PaymentExceedsRemainingException(remaining);
        }
    }

    public void Cancel()
    {
        if (IsCancelled)
        {
            throw new DebtAlreadyCancelledException();
        }

        CancelledAt = DateTimeOffset.UtcNow;
        UpdatedAt = CancelledAt.Value;
        Raise(new DebtCancelled(Id));
    }

    /// <summary>ADR-0010 §8: a debt with any linked repayment (even of a deleted operation) is cancelled, not deleted.</summary>
    public void EnsureCanBeDeleted()
    {
        if (_repayments.Count > 0)
        {
            throw new DebtHasRepaymentsException();
        }
    }

    public void MarkAsDeleted() => Raise(new DebtDeleted(Id));

    private void EnsureActive()
    {
        if (IsCancelled)
        {
            throw new DebtNotActiveException();
        }
    }

    internal static void EnsurePositive(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new DebtsAmountMustBePositiveException();
        }
    }
}

/// <summary>Link between a debt and the expense operation that repaid it; the amount is as recorded.</summary>
public sealed class DebtRepayment
{
    private decimal _amount;

    public Guid Id { get; private set; }
    public OperationId OperationId { get; private set; }
    public WalletId WalletId { get; private set; }
    public DateOnly RepaymentDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public decimal Amount => _amount;

    private DebtRepayment()
    {
        // EF Core only.
    }

    internal static DebtRepayment Create(OperationId operationId, WalletId walletId, decimal amount, DateOnly repaymentDate) => new()
    {
        Id = Guid.NewGuid(),
        OperationId = operationId,
        WalletId = walletId,
        _amount = amount,
        RepaymentDate = repaymentDate,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
