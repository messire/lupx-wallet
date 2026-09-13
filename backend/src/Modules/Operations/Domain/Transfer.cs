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

    public Money Amount => new(_amount, CurrencyId);

    private Transfer()
    {
        // Только для EF Core.
    }

    /// <summary>
    /// Creates the transfer and both its operations atomically in memory (persistence of both
    /// goes through Application/UnitOfWork in one transaction). Source/target wallet currency
    /// match is checked by the caller (Application, ADR-0007) — here Money is already
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

    /// <summary>Check before physical deletion — the actual decision is made by the Application layer.</summary>
    public void EnsureCanBeDeleted(bool hasHistory)
    {
        if (hasHistory)
        {
            throw new TransferDeletionNotAllowedException(Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new TransferDeleted(Id));
}
