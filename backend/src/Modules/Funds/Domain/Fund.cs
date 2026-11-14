using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Domain;

/// <summary>
/// Fund aggregate (ADR-0010 §4): money of one currency set aside in one or several wallets.
/// A reserve never changes wallet balances. The free-funds check of a new reserve is a
/// collection invariant over all funds and is done by ReservationPolicy (Application) before
/// <see cref="Reserve"/> is called.
/// </summary>
public sealed class Fund : AggregateRoot<FundId>
{
    public const int MaxNameLength = 200;

    private readonly List<FundAllocation> _allocations = [];
    private readonly List<FundLinkedRelease> _linkedReleases = [];

    public string Name { get; private set; } = null!;
    public decimal TargetAmount { get; private set; }
    public CurrencyId CurrencyId { get; private set; }

    /// <summary>Optional link to a debt (Debts module). Plain id, not validated here.</summary>
    public Guid? LinkedDebtId { get; private set; }

    /// <summary>Optional link to a loan (Debts module). Plain id, not validated here.</summary>
    public Guid? LinkedLoanId { get; private set; }

    public FundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public IReadOnlyList<FundAllocation> Allocations => _allocations.AsReadOnly();

    /// <summary>Reserves released by linked debt/loan payments, per payment operation and wallet (B9).</summary>
    public IReadOnlyList<FundLinkedRelease> LinkedReleases => _linkedReleases.AsReadOnly();

    public Money Target => new(TargetAmount, CurrencyId);

    /// <summary>Sum of all allocations.</summary>
    public Money Reserved => new(_allocations.Sum(a => a.Amount), CurrencyId);

    /// <summary>True once any reserve was ever made; such a fund is closed, not deleted (ADR-0010 §8).</summary>
    public bool HasAllocationHistory => _allocations.Count > 0;

    public bool IsActive => Status == FundStatus.Active;

    private Fund()
    {
        // EF Core only.
    }

    public static Fund Create(
        FundId id, string name, decimal targetAmount, CurrencyId currencyId, Guid? linkedDebtId, Guid? linkedLoanId)
    {
        ValidateDetails(name, targetAmount, linkedDebtId, linkedLoanId);

        var now = DateTimeOffset.UtcNow;
        var fund = new Fund
        {
            Id = id,
            Name = name.Trim(),
            TargetAmount = targetAmount,
            CurrencyId = currencyId,
            LinkedDebtId = linkedDebtId,
            LinkedLoanId = linkedLoanId,
            Status = FundStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        fund.Raise(new FundCreated(id));
        return fund;
    }

    /// <summary>Full replacement of the editable fields. The currency can change only before the first reserve.</summary>
    public void Update(string name, decimal targetAmount, CurrencyId currencyId, Guid? linkedDebtId, Guid? linkedLoanId)
    {
        EnsureActive();
        ValidateDetails(name, targetAmount, linkedDebtId, linkedLoanId);

        if (currencyId != CurrencyId && HasAllocationHistory)
        {
            throw new FundCurrencyChangeNotAllowedException();
        }

        Name = name.Trim();
        TargetAmount = targetAmount;
        CurrencyId = currencyId;
        LinkedDebtId = linkedDebtId;
        LinkedLoanId = linkedLoanId;
        UpdatedAt = DateTimeOffset.UtcNow;

        Raise(new FundUpdated(Id));
    }

    /// <summary>Allocation amount for the wallet (0 if none).</summary>
    public decimal AllocatedOn(WalletId walletId) => FindAllocation(walletId)?.Amount ?? 0m;

    /// <summary>
    /// Creates or increases the allocation on the wallet. The caller (ReservationPolicy) has already
    /// checked that <paramref name="amount"/> fits into the wallet's free funds.
    /// </summary>
    public void Reserve(WalletId walletId, CurrencyId walletCurrencyId, decimal amount)
    {
        EnsureCanReserve(walletCurrencyId, amount);

        var now = DateTimeOffset.UtcNow;
        var allocation = FindAllocation(walletId);
        if (allocation is null)
        {
            allocation = FundAllocation.Create(Id, walletId, now);
            _allocations.Add(allocation);
        }

        var old = allocation.Amount;
        allocation.SetAmount(old + amount, now);
        UpdatedAt = now;

        Raise(new FundReserved(Id, walletId, CurrencyId, amount, old, allocation.Amount));
    }

    /// <summary>Own-state checks of a reserve, run by ReservationPolicy before the free-funds check.</summary>
    public void EnsureCanReserve(CurrencyId walletCurrencyId, decimal amount)
    {
        EnsureActive();
        EnsurePositive(amount);

        if (walletCurrencyId != CurrencyId)
        {
            throw new FundWalletCurrencyMismatchException();
        }
    }

    /// <summary>Decreases the allocation on the wallet by exactly <paramref name="amount"/>.</summary>
    public void Release(WalletId walletId, decimal amount)
    {
        EnsureActive();
        EnsurePositive(amount);

        var allocated = AllocatedOn(walletId);
        if (amount > allocated)
        {
            throw new FundReleaseExceedsAllocationException(allocated);
        }

        Decrease(FindAllocation(walletId)!, amount, FundReleaseReason.Manual, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// ADR-0010 §4, B9: a payment of the linked debt/loan releases the reserve on the paying wallet,
    /// not more than the allocation. Returns the released amount (0 for a closed fund or no allocation).
    /// </summary>
    public decimal ReleaseUpTo(WalletId walletId, decimal amount)
    {
        EnsurePositive(amount);

        var allocation = FindAllocation(walletId);
        if (!IsActive || allocation is null || allocation.Amount == 0m)
        {
            return 0m;
        }

        var released = Math.Min(amount, allocation.Amount);
        Decrease(allocation, released, FundReleaseReason.LinkedPayment, DateTimeOffset.UtcNow);
        return released;
    }

    /// <summary>
    /// <see cref="ReleaseUpTo"/> for the payment operation, remembering how much this operation
    /// released so that deleting the expense can bring it back (<see cref="RestoreLinkedRelease"/>).
    /// </summary>
    public decimal ReleaseForPayment(WalletId walletId, OperationId operationId, decimal amount)
    {
        EnsurePositive(amount);

        var allocation = FindAllocation(walletId);
        if (!IsActive || allocation is null || allocation.Amount == 0m)
        {
            return 0m;
        }

        var now = DateTimeOffset.UtcNow;
        var released = Math.Min(amount, allocation.Amount);
        Decrease(allocation, released, FundReleaseReason.LinkedPayment, now, operationId);

        var record = FindLinkedRelease(operationId, walletId);
        if (record is null)
        {
            record = FundLinkedRelease.Create(Id, operationId, walletId, now);
            _linkedReleases.Add(record);
        }

        record.SetAmount(record.Amount + released, now);
        return released;
    }

    /// <summary>Reserve the operation currently keeps released on the wallet (0 if none).</summary>
    public decimal ReleasedBy(OperationId operationId, WalletId walletId) => FindLinkedRelease(operationId, walletId)?.Amount ?? 0m;

    /// <summary>
    /// The payment expense was soft-deleted (ADR-0010 §4, B9): gives back what it released on the
    /// wallet, not more than <paramref name="maxAmount"/> (the wallet's free funds, checked by the
    /// caller). Returns the restored amount; 0 for a closed fund.
    /// </summary>
    public decimal RestoreLinkedRelease(OperationId operationId, WalletId walletId, decimal maxAmount)
    {
        var record = FindLinkedRelease(operationId, walletId);
        if (!IsActive || record is null || record.Amount == 0m || maxAmount <= 0m)
        {
            return 0m;
        }

        var restored = Math.Min(record.Amount, maxAmount);
        var now = DateTimeOffset.UtcNow;
        var allocation = FindAllocation(walletId)!;
        var old = allocation.Amount;
        allocation.SetAmount(old + restored, now);
        record.SetAmount(record.Amount - restored, now);
        UpdatedAt = now;

        Raise(new FundReserved(Id, walletId, CurrencyId, restored, old, allocation.Amount)
        {
            Reason = FundReserveReason.LinkedPaymentDeleted,
            OperationId = operationId,
        });
        return restored;
    }

    /// <summary>Closes the fund and releases all allocations (ADR-0010 §4). Closed is final.</summary>
    public void Close()
    {
        EnsureActive();

        var now = DateTimeOffset.UtcNow;
        foreach (var allocation in _allocations.Where(a => a.Amount != 0m))
        {
            Decrease(allocation, allocation.Amount, FundReleaseReason.FundClosed, now);
        }

        Status = FundStatus.Closed;
        ClosedAt = now;
        UpdatedAt = now;
        Raise(new FundClosed(Id));
    }

    /// <summary>A fund with allocation history is closed, not deleted (ADR-0010 §8).</summary>
    public void EnsureCanBeDeleted()
    {
        if (HasAllocationHistory)
        {
            throw new FundDeletionNotAllowedException();
        }
    }

    public void MarkAsDeleted() => Raise(new FundDeleted(Id));

    private void Decrease(
        FundAllocation allocation, decimal amount, FundReleaseReason reason, DateTimeOffset now, OperationId? operationId = null)
    {
        var old = allocation.Amount;
        allocation.SetAmount(old - amount, now);
        UpdatedAt = now;
        Raise(new FundReleased(Id, allocation.WalletId, CurrencyId, amount, old, allocation.Amount, reason) { OperationId = operationId });
    }

    private FundAllocation? FindAllocation(WalletId walletId) => _allocations.FirstOrDefault(a => a.WalletId == walletId);

    private FundLinkedRelease? FindLinkedRelease(OperationId operationId, WalletId walletId) =>
        _linkedReleases.FirstOrDefault(r => r.OperationId == operationId && r.WalletId == walletId);

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new FundClosedException();
        }
    }

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new FundAmountMustBePositiveException();
        }
    }

    private static void ValidateDetails(string name, decimal targetAmount, Guid? linkedDebtId, Guid? linkedLoanId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FundNameRequiredException();
        }

        if (name.Trim().Length > MaxNameLength)
        {
            throw new FundNameTooLongException(MaxNameLength);
        }

        if (targetAmount <= 0m)
        {
            throw new FundTargetAmountMustBePositiveException();
        }

        if (linkedDebtId is not null && linkedLoanId is not null)
        {
            throw new FundLinkAmbiguousException();
        }
    }
}
