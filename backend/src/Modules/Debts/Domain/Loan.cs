using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Domain;

/// <summary>
/// Loan with a payment schedule (ADR-0010 §3, issue #8). No interest (B5): schedule and payment
/// amounts are amounts to pay. Payments are links to regular expense operations; which of them
/// count is decided by the caller from operation facts, so the remaining amount, the Repaid
/// status and the schedule item statuses are computed (<see cref="LoanScheduleCalculator"/>).
/// </summary>
public sealed class Loan : AggregateRoot<LoanId>
{
    /// <summary>Upper bound for a generated schedule (50 years of monthly payments).</summary>
    public const int MaxGeneratedPayments = 600;

    private readonly List<LoanScheduleItem> _scheduleItems = [];
    private readonly List<LoanPayment> _payments = [];
    private decimal _amountToRepay;

    public CreditorId CreditorId { get; private set; }
    public string Name { get; private set; } = null!;
    public CurrencyId CurrencyId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Money AmountToRepay => new(_amountToRepay, CurrencyId);
    public bool IsCancelled => CancelledAt is not null;
    public IReadOnlyList<LoanPayment> Payments => _payments.AsReadOnly();

    public IReadOnlyList<LoanScheduleItem> ScheduleItems => _scheduleItems.AsReadOnly();

    /// <summary>Schedule ordered by due date (ties keep the entered order).</summary>
    public IReadOnlyList<LoanScheduleEntry> OrderedSchedule() =>
        _scheduleItems.OrderBy(x => x.DueDate).ThenBy(x => x.Position).Select(x => x.ToEntry()).ToList();

    private Loan()
    {
        // EF Core only.
    }

    public static Loan Create(LoanId id, CreditorId creditorId, string name, Money amountToRepay, DateOnly startDate)
    {
        Debt.EnsurePositive(amountToRepay.Amount);
        var now = DateTimeOffset.UtcNow;
        var loan = new Loan
        {
            Id = id,
            CreditorId = creditorId,
            Name = Creditor.RequireName(name),
            CurrencyId = amountToRepay.CurrencyId,
            _amountToRepay = amountToRepay.Amount,
            StartDate = startDate,
            CreatedAt = now,
            UpdatedAt = now,
        };

        loan.Raise(new LoanCreated(id));
        return loan;
    }

    /// <summary>Changing terms (#8). Currency is fixed at creation.</summary>
    public void Update(CreditorId creditorId, string name, decimal amountToRepay, DateOnly startDate)
    {
        EnsureActive();
        Debt.EnsurePositive(amountToRepay);

        CreditorId = creditorId;
        Name = Creditor.RequireName(name);
        _amountToRepay = amountToRepay;
        StartDate = startDate;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new LoanUpdated(Id));
    }

    /// <summary>Replaces the whole schedule (reschedule a date, change amounts, add or remove items).</summary>
    public void ReplaceSchedule(IReadOnlyList<LoanScheduleEntry> entries)
    {
        EnsureActive();
        if (entries.Any(x => x.Amount <= 0m))
        {
            throw new LoanScheduleItemAmountMustBePositiveException();
        }

        var previous = OrderedSchedule();
        _scheduleItems.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            _scheduleItems.Add(LoanScheduleItem.Create(entries[i].DueDate, entries[i].Amount, i));
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new LoanScheduleChanged(Id, previous, OrderedSchedule()));
    }

    /// <summary>Replaces the schedule with N equal monthly payments of the amount to repay.</summary>
    public void GenerateSchedule(DateOnly firstPaymentDate, int numberOfPayments) =>
        ReplaceSchedule(LoanScheduleCalculator.GenerateEqualMonthly(_amountToRepay, firstPaymentDate, numberOfPayments));

    public Money RemainingAmount(decimal countedPaid) => new(Math.Max(0m, _amountToRepay - countedPaid), CurrencyId);

    public ObligationStatus Status(decimal countedPaid) =>
        IsCancelled ? ObligationStatus.Cancelled
        : _amountToRepay - countedPaid <= 0m ? ObligationStatus.Repaid
        : ObligationStatus.Active;

    public IReadOnlyList<LoanScheduleItemState> EvaluateSchedule(decimal countedPaid, DateOnly today) =>
        LoanScheduleCalculator.Evaluate(OrderedSchedule(), countedPaid, today);

    public LoanPayment RecordPayment(
        OperationId operationId, WalletId walletId, Money amount, DateOnly paymentDate, decimal countedPaid)
    {
        EnsureCanAcceptPayment(amount, countedPaid);

        var payment = LoanPayment.Create(operationId, walletId, amount.Amount, paymentDate);
        _payments.Add(payment);
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new LoanPaymentRecorded(Id, walletId, operationId, amount) { PaymentDate = paymentDate });
        return payment;
    }

    public void EnsureCanAcceptPayment(Money amount, decimal countedPaid)
    {
        EnsureActive();
        if (amount.CurrencyId != CurrencyId)
        {
            throw new PaymentCurrencyMismatchException();
        }

        Debt.EnsurePositive(amount.Amount);
        var remaining = RemainingAmount(countedPaid).Amount;
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
            throw new LoanAlreadyCancelledException();
        }

        CancelledAt = DateTimeOffset.UtcNow;
        UpdatedAt = CancelledAt.Value;
        Raise(new LoanCancelled(Id));
    }

    /// <summary>ADR-0010 §8: a loan with any linked payment is cancelled, not deleted.</summary>
    public void EnsureCanBeDeleted()
    {
        if (_payments.Count > 0)
        {
            throw new LoanHasPaymentsException();
        }
    }

    public void MarkAsDeleted() => Raise(new LoanDeleted(Id));

    private void EnsureActive()
    {
        if (IsCancelled)
        {
            throw new LoanNotActiveException();
        }
    }
}

/// <summary>Schedule item value (due date + amount to pay).</summary>
public sealed record LoanScheduleEntry(DateOnly DueDate, decimal Amount);

/// <summary>Computed state of a schedule item.</summary>
public sealed record LoanScheduleItemState(DateOnly DueDate, decimal Amount, decimal PaidAmount, LoanScheduleItemStatus Status)
{
    public decimal OutstandingAmount => Amount - PaidAmount;
}

public sealed class LoanScheduleItem
{
    private decimal _amount;

    public Guid Id { get; private set; }
    public DateOnly DueDate { get; private set; }

    /// <summary>Entered order, breaks ties between items with the same date.</summary>
    public int Position { get; private set; }

    public decimal Amount => _amount;

    private LoanScheduleItem()
    {
        // EF Core only.
    }

    internal static LoanScheduleItem Create(DateOnly dueDate, decimal amount, int position) => new()
    {
        Id = Guid.NewGuid(),
        DueDate = dueDate,
        _amount = amount,
        Position = position,
    };

    internal LoanScheduleEntry ToEntry() => new(DueDate, _amount);
}

/// <summary>Link between a loan and the expense operation that paid it; the amount is as recorded.</summary>
public sealed class LoanPayment
{
    private decimal _amount;

    public Guid Id { get; private set; }
    public OperationId OperationId { get; private set; }
    public WalletId WalletId { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public decimal Amount => _amount;

    private LoanPayment()
    {
        // EF Core only.
    }

    internal static LoanPayment Create(OperationId operationId, WalletId walletId, decimal amount, DateOnly paymentDate) => new()
    {
        Id = Guid.NewGuid(),
        OperationId = operationId,
        WalletId = walletId,
        _amount = amount,
        PaymentDate = paymentDate,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
