using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Агрегат Transfer (ddd-model.md, §2.5) — координирует создание ровно двух связанных
/// Operation (списание/зачисление) как единого действия (раздел 4 требований). Хранит
/// только ссылки на обе операции, не встраивает их — это сознательный компромисс:
/// сама Operation остаётся полноценным агрегатом в своём хранилище, атомарность
/// обеспечивается на уровне Application/UnitOfWork в рамках одной транзакции.
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
    /// Создаёт перевод и обе его операции атомарно (в памяти — сохранение обеих через
    /// Application/UnitOfWork в одной транзакции). Совпадение валют исходного/целевого
    /// кошельков проверяется вызывающим кодом (Application, ADR-0007) — здесь работаем
    /// с уже согласованной Money на одну валюту.
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

    /// <summary>Проверка перед физическим удалением — само решение принимает Application-слой.</summary>
    public void EnsureCanBeDeleted(bool hasHistory)
    {
        if (hasHistory)
        {
            throw new TransferDeletionNotAllowedException(Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new TransferDeleted(Id));
}
