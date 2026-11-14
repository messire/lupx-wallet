using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Domain;

/// <summary>
/// PlannedTransaction aggregate (ADR-0010 §6): a future income or expense, one-off or recurring.
/// Occurrences are computed from the first date and the rule; only deviations (OccurrenceOverride)
/// and executions (OccurrenceExecution) are stored. Until executed, a plan changes no balance.
/// </summary>
public sealed class PlannedTransaction : AggregateRoot<PlannedTransactionId>
{
    public const int DescriptionMaxLength = 500;

    private readonly List<OccurrenceOverride> _overrides = [];
    private readonly List<OccurrenceExecution> _executions = [];

    public OperationTypeId OperationTypeId { get; private set; }
    public PlannedDirection Direction { get; private set; }
    public decimal Amount { get; private set; }
    public CurrencyId CurrencyId { get; private set; }

    /// <summary>Default wallet for execution; always in the plan currency.</summary>
    public WalletId? WalletId { get; private set; }

    public string? Description { get; private set; }
    public DateOnly StartDate { get; private set; }

    // RecurrenceRule is stored flat (one nullable column per part); null frequency — a one-off plan.
    public RecurrenceFrequency? RecurrenceFrequency { get; private set; }
    public int? RecurrenceInterval { get; private set; }
    public DateOnly? RecurrenceUntil { get; private set; }
    public int? RecurrenceCount { get; private set; }

    public PlannedTransactionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public RecurrenceRule? Recurrence => RecurrenceFrequency is { } frequency
        ? RecurrenceRule.Create(frequency, RecurrenceInterval ?? 1, RecurrenceUntil, RecurrenceCount)
        : null;

    public IReadOnlyCollection<OccurrenceOverride> Overrides => _overrides.AsReadOnly();
    public IReadOnlyCollection<OccurrenceExecution> Executions => _executions.AsReadOnly();

    public bool HasExecutions => _executions.Count > 0;
    public bool IsActive => Status == PlannedTransactionStatus.Active;

    private PlannedTransaction()
    {
        // Только для EF Core.
    }

    public static PlannedTransaction Create(
        PlannedTransactionId id,
        OperationTypeId operationTypeId,
        PlannedDirection direction,
        decimal amount,
        CurrencyId currencyId,
        WalletId? walletId,
        string? description,
        DateOnly startDate,
        RecurrenceRule? recurrence)
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new PlannedTransaction
        {
            Id = id,
            Status = PlannedTransactionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        plan.Apply(operationTypeId, direction, amount, currencyId, walletId, description, startDate, recurrence);
        plan.Raise(new PlannedTransactionCreated(id));
        return plan;
    }

    /// <summary>
    /// Replaces the plan fields. With executions, the currency and the direction are fixed and every
    /// executed occurrence must stay in the new schedule; overrides of dates that leave it are dropped.
    /// </summary>
    public void Update(
        OperationTypeId operationTypeId,
        PlannedDirection direction,
        decimal amount,
        CurrencyId currencyId,
        WalletId? walletId,
        string? description,
        DateOnly startDate,
        RecurrenceRule? recurrence)
    {
        EnsureActive();

        if (HasExecutions)
        {
            if (currencyId != CurrencyId)
            {
                throw new PlannedCurrencyChangeNotAllowedException();
            }

            if (direction != Direction)
            {
                throw new PlannedDirectionChangeNotAllowedException();
            }

            if (_executions.Any(e => !OccurrenceSchedule.IsOccurrence(startDate, recurrence, e.OccurrenceDate)))
            {
                throw new PlannedScheduleChangeConflictException();
            }
        }

        Apply(operationTypeId, direction, amount, currencyId, walletId, description, startDate, recurrence);
        var removed = _overrides.Where(o => !IsOccurrence(o.OccurrenceDate)).ToList();
        _overrides.RemoveAll(removed.Contains);
        Touch();
        Raise(new PlannedTransactionUpdated(Id)
        {
            RemovedOverrides = removed
                .OrderBy(o => o.OccurrenceDate)
                .Select(o => new OccurrenceOverrideSnapshot(o.OccurrenceDate, o.IsSkipped, o.MovedToDate, o.Amount))
                .ToList(),
        });
    }

    public void Finish()
    {
        EnsureActive();
        var now = DateTimeOffset.UtcNow;
        Status = PlannedTransactionStatus.Finished;
        FinishedAt = now;
        UpdatedAt = now;
        Raise(new PlannedTransactionFinished(Id));
    }

    /// <summary>Physical deletion is allowed only without executions (ADR-0010 §8); otherwise finish the plan.</summary>
    public void MarkAsDeleted()
    {
        if (HasExecutions)
        {
            throw new PlannedTransactionHasExecutionsException();
        }

        Raise(new PlannedTransactionDeleted(Id));
    }

    /// <summary>Drops the default wallet (it was deleted or changed currency); allowed for a finished plan too.</summary>
    public void UnlinkWallet()
    {
        if (WalletId is null)
        {
            return;
        }

        WalletId = null;
        Touch();
        Raise(new PlannedTransactionUpdated(Id));
    }

    public bool IsOccurrence(DateOnly occurrenceDate) => OccurrenceSchedule.IsOccurrence(StartDate, Recurrence, occurrenceDate);

    public OccurrenceOverride? FindOverride(DateOnly occurrenceDate) =>
        _overrides.FirstOrDefault(o => o.OccurrenceDate == occurrenceDate);

    public void SkipOccurrence(PlannedOccurrence current)
    {
        EnsureCanChange(current);
        if (current.Status == PlannedOccurrenceStatus.Skipped)
        {
            return;
        }

        if (current.Status == PlannedOccurrenceStatus.Executed)
        {
            throw new PlannedOccurrenceAlreadyExecutedException();
        }

        ChangeOverride(current.OccurrenceDate, o => o.SetSkipped(true));
        Raise(new PlannedOccurrenceSkipped(Id, current.OccurrenceDate));
    }

    public void UnskipOccurrence(PlannedOccurrence current)
    {
        EnsureCanChange(current);
        if (current.Status != PlannedOccurrenceStatus.Skipped)
        {
            throw new PlannedOccurrenceNotSkippedException();
        }

        ChangeOverride(current.OccurrenceDate, o => o.SetSkipped(false));
        Raise(new PlannedOccurrenceUnskipped(Id, current.OccurrenceDate));
    }

    /// <summary>Moves the occurrence to <paramref name="newDate"/>; the original date undoes the move.</summary>
    public void MoveOccurrence(PlannedOccurrence current, DateOnly newDate)
    {
        EnsureCanChange(current);
        if (newDate == current.EffectiveDate)
        {
            return;
        }

        ChangeOverride(current.OccurrenceDate, o => o.SetMovedTo(newDate == current.OccurrenceDate ? null : newDate));
        Raise(new PlannedOccurrenceMoved(Id, current.OccurrenceDate, current.EffectiveDate, newDate));
    }

    /// <summary>Sets the amount of one occurrence; null returns it to the plan amount.</summary>
    public void ChangeOccurrenceAmount(PlannedOccurrence current, decimal? amount)
    {
        EnsureCanChange(current);
        if (amount is { } value && value <= 0)
        {
            throw new PlannedAmountMustBePositiveException();
        }

        var oldAmount = FindOverride(current.OccurrenceDate)?.Amount;
        var newAmount = amount == Amount ? null : amount;
        if (oldAmount == newAmount)
        {
            return;
        }

        ChangeOverride(current.OccurrenceDate, o => o.SetAmount(newAmount));
        Raise(new PlannedOccurrenceAmountChanged(Id, current.OccurrenceDate, oldAmount, newAmount));
    }

    /// <summary>
    /// Links a real operation to the occurrence. A partial amount keeps the remainder expected
    /// (ADR-0010 §6); a skipped or fully executed occurrence cannot be executed.
    /// </summary>
    public OccurrenceExecution RecordExecution(PlannedOccurrence current, OperationId operationId, decimal amount)
    {
        EnsureCanExecute(current);
        if (amount <= 0)
        {
            throw new PlannedAmountMustBePositiveException();
        }

        var execution = new OccurrenceExecution(current.OccurrenceDate, operationId, amount, DateTimeOffset.UtcNow);
        _executions.Add(execution);
        Touch();
        Raise(new PlannedOccurrenceExecuted(Id, current.OccurrenceDate, operationId, amount));
        return execution;
    }

    /// <summary>Checked before the operation is created, so a rejected execution has no side effects.</summary>
    public void EnsureCanExecute(PlannedOccurrence current)
    {
        EnsureCanChange(current);
        switch (current.Status)
        {
            case PlannedOccurrenceStatus.Skipped:
                throw new PlannedOccurrenceSkippedException();
            case PlannedOccurrenceStatus.Executed:
                throw new PlannedOccurrenceAlreadyExecutedException();
        }
    }

    private void Apply(
        OperationTypeId operationTypeId,
        PlannedDirection direction,
        decimal amount,
        CurrencyId currencyId,
        WalletId? walletId,
        string? description,
        DateOnly startDate,
        RecurrenceRule? recurrence)
    {
        if (amount <= 0)
        {
            throw new PlannedAmountMustBePositiveException();
        }

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalizedDescription is { Length: > DescriptionMaxLength })
        {
            throw new PlannedDescriptionTooLongException(DescriptionMaxLength);
        }

        if (recurrence?.Until is { } until && until < startDate)
        {
            throw new RecurrenceUntilBeforeStartException();
        }

        OperationTypeId = operationTypeId;
        Direction = direction;
        Amount = amount;
        CurrencyId = currencyId;
        WalletId = walletId;
        Description = normalizedDescription;
        StartDate = startDate;
        RecurrenceFrequency = recurrence?.Frequency;
        RecurrenceInterval = recurrence?.Interval;
        RecurrenceUntil = recurrence?.Until;
        RecurrenceCount = recurrence?.Count;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new PlannedTransactionFinishedException();
        }
    }

    private void EnsureCanChange(PlannedOccurrence current)
    {
        EnsureActive();
        if (current.PlannedTransactionId != Id)
        {
            throw new ArgumentException("The occurrence belongs to another planned transaction.", nameof(current));
        }
    }

    private void ChangeOverride(DateOnly occurrenceDate, Action<OccurrenceOverride> change)
    {
        var existing = FindOverride(occurrenceDate);
        var target = existing ?? new OccurrenceOverride(occurrenceDate);
        change(target);

        if (target.IsEmpty)
        {
            if (existing is not null)
            {
                _overrides.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _overrides.Add(target);
        }

        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
