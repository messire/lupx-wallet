using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Transfer aggregate (ddd-model.md, §2.5) — coordinates creating exactly two related
/// Operation rows (debit/credit) as a single action (requirements, §4). Holds only
/// references to both operations rather than embedding them — a deliberate trade-off: each
/// Operation stays a full aggregate in its own store, and atomicity is provided at the
/// Application/UnitOfWork level within one transaction.
/// </summary>
public sealed class Transfer : AggregateRoot<TransferId>
{
    private decimal _amount;

    public WalletId SourceWalletId { get; private set; }
    public WalletId TargetWalletId { get; private set; }
    public OperationId SourceOperationId { get; private set; }
    public OperationId TargetOperationId { get; private set; }
    public CurrencyId CurrencyId { get; private set; }
    public DateOnly TransferDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Soft-deletion moment (ADR-0002, §3), shared with both legs; null while active.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public Money Amount => new(_amount, CurrencyId);

    private Transfer()
    {
        // Только для EF Core.
    }

    /// <summary>
    /// Creates the transfer and both its operations atomically in memory (persistence of both
    /// goes through Application/UnitOfWork in one transaction). Source/target wallet currency
    /// match is checked by the caller (Application, ADR-0001) — here Money is already
    /// reconciled to a single currency.
    /// </summary>
    public static (Transfer Transfer, Operation SourceOperation, Operation TargetOperation) Create(
        TransferId id,
        WalletId sourceWalletId,
        WalletId targetWalletId,
        OperationTypeId operationTypeId,
        Money amount,
        DateOnly transferDate)
    {
        if (sourceWalletId == targetWalletId)
        {
            throw new TransferSameWalletException();
        }

        if (amount.Amount <= 0)
        {
            throw new OperationAmountMustBePositiveException();
        }

        var now = DateTimeOffset.UtcNow;
        var sourceOperationId = OperationId.New();
        var targetOperationId = OperationId.New();

        var transfer = new Transfer
        {
            Id = id,
            SourceWalletId = sourceWalletId,
            TargetWalletId = targetWalletId,
            SourceOperationId = sourceOperationId,
            TargetOperationId = targetOperationId,
            CurrencyId = amount.CurrencyId,
            _amount = amount.Amount,
            TransferDate = transferDate,
            CreatedAt = now,
        };

        // Решено, Q10: сумма списания всегда равна сумме зачисления — комиссия не поддерживается.
        var sourceOperation = Operation.CreateForTransfer(
            sourceOperationId, sourceWalletId, operationTypeId, amount, transferDate, amount.Negate(), id);
        var targetOperation = Operation.CreateForTransfer(
            targetOperationId, targetWalletId, operationTypeId, amount, transferDate, amount, id);

        transfer.Raise(new TransferCreated(id, sourceWalletId, targetWalletId));
        return (transfer, sourceOperation, targetOperation);
    }

    /// <summary>
    /// UC-16, ADR-0002 §3 — soft-deletes the transfer and both legs as a whole, any date. All
    /// three rows get the same DeletedAt. The caller reverses each leg's AppliedDelta on its wallet.
    /// </summary>
    public void SoftDelete(Operation sourceOperation, Operation targetOperation)
    {
        EnsureLegs(sourceOperation, targetOperation);

        if (IsDeleted)
        {
            throw new TransferAlreadyDeletedException(Id.Value);
        }

        var deletedAt = DateTimeOffset.UtcNow;
        sourceOperation.SoftDeleteAsTransferLeg(deletedAt);
        targetOperation.SoftDeleteAsTransferLeg(deletedAt);

        DeletedAt = deletedAt;
        Raise(new TransferDeleted(Id));
    }

    /// <summary>Revokes soft deletion of the transfer and both legs; the caller re-applies each leg's AppliedDelta.</summary>
    public void Restore(Operation sourceOperation, Operation targetOperation)
    {
        EnsureLegs(sourceOperation, targetOperation);

        if (!IsDeleted)
        {
            throw new TransferNotDeletedException(Id.Value);
        }

        sourceOperation.RestoreAsTransferLeg();
        targetOperation.RestoreAsTransferLeg();

        DeletedAt = null;
        Raise(new TransferRestored(Id));
    }

    private void EnsureLegs(Operation sourceOperation, Operation targetOperation)
    {
        if (sourceOperation.Id != SourceOperationId || targetOperation.Id != TargetOperationId)
        {
            throw new TransferLegMismatchException(Id.Value);
        }
    }
}
