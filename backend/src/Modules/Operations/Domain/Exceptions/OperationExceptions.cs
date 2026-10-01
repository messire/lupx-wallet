namespace LupexWallet.Operations.Domain;

public abstract class OperationsDomainException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class AdjustmentModeRequiredException()
    : OperationsDomainException("Для операции с поведением Adjustment обязателен способ ввода (Absolute/Delta).");

public sealed class AdjustmentModeNotAllowedException()
    : OperationsDomainException("Способ ввода корректировки (adjustmentMode) допустим только для операций с поведением Adjustment.");

public sealed class OperationAmountMustBePositiveException()
    : OperationsDomainException("Сумма операции дохода/расхода должна быть положительной — направление определяется поведением типа операции.");

public sealed class DirectOperationCreationNotAllowedForBehaviorException(string behaviorCode)
    : OperationsDomainException($"Операцию с поведением '{behaviorCode}' нельзя создать напрямую через /operations — используйте соответствующий эндпоинт (например, /transfers для Transfer).");

public sealed class OperationCurrencyImmutableException()
    : OperationsDomainException("Валюта операции неизменяема после создания (совпадает с валютой кошелька на момент создания).");

public sealed class TransferSameWalletException()
    : OperationsDomainException("Исходный и целевой кошельки перевода должны различаться.");

public sealed class TransferCurrencyMismatchException()
    : OperationsDomainException("Перевод разрешен только между кошельками одной валюты.");

// Soft-delete lifecycle conflicts (ADR-0002, §3) — all map to HTTP 409. The message goes to
// Problem.detail and is shown to the user as is, so it carries no identifiers.

public sealed class OperationPartOfTransferException(Guid operationId)
    : OperationsDomainException("Операция входит в перевод: изменяйте, удаляйте и восстанавливайте перевод целиком.")
{
    public Guid OperationId { get; } = operationId;
}

public sealed class OperationAlreadyDeletedException(Guid operationId)
    : OperationsDomainException("Операция уже удалена.")
{
    public Guid OperationId { get; } = operationId;
}

public sealed class OperationNotDeletedException(Guid operationId)
    : OperationsDomainException("Операция не удалена — восстанавливать нечего.")
{
    public Guid OperationId { get; } = operationId;
}

/// <summary>A soft-deleted operation cannot be edited or moved until restored.</summary>
public sealed class OperationIsDeletedException(Guid operationId)
    : OperationsDomainException("Операция удалена — сначала восстановите её.")
{
    public Guid OperationId { get; } = operationId;
}

public sealed class TransferAlreadyDeletedException(Guid transferId)
    : OperationsDomainException("Перевод уже удалён.")
{
    public Guid TransferId { get; } = transferId;
}

public sealed class TransferNotDeletedException(Guid transferId)
    : OperationsDomainException("Перевод не удалён — восстанавливать нечего.")
{
    public Guid TransferId { get; } = transferId;
}

/// <summary>
/// Internal invariant violation (corrupted data, not a client error): the operations passed to
/// Transfer.SoftDelete/Restore are not this transfer's legs. Answered with 500; the message is
/// for logs only.
/// </summary>
public sealed class TransferLegMismatchException(Guid transferId)
    : OperationsDomainException($"Переданные операции не являются «ногами» перевода ({transferId}).");

public sealed class NoActiveOperationTypeForTransferException()
    : OperationsDomainException("Нет ни одного активного типа операции с поведением Transfer — создайте его в справочнике типов операций перед переводом.");

public sealed class OperationTypeReferenceNotFoundException(Guid operationTypeId)
    : OperationsDomainException("Тип операции не найден.")
{
    public Guid OperationTypeId { get; } = operationTypeId;
}

public sealed class OperationTypeReferenceInactiveException(Guid operationTypeId)
    : OperationsDomainException("Тип операции деактивирован — нельзя ссылаться на него в новой операции.")
{
    public Guid OperationTypeId { get; } = operationTypeId;
}

public sealed class WalletReferenceNotFoundException(Guid walletId)
    : OperationsDomainException("Кошелек не найден.")
{
    public Guid WalletId { get; } = walletId;
}

/// <summary>
/// Operation/transfer date must be within [Wallet.AccountingStartDate .. today] (UTC) — the
/// same bounds that already govern balance history materialization (Q13: "balance before
/// accounting_start_date is zero, snapshots are not materialized"; today is the upper bound
/// of the daily snapshot, requirements §5). Without this check, an out-of-range operation
/// would still change Wallet.CurrentBalance (synchronously, ADR-0001) but would not be
/// correctly reflected in the balance history — a discrepancy found while implementing
/// BalanceHistory (ADR-0003).
/// </summary>
public sealed class OperationDateOutOfRangeException(DateOnly operationDate, DateOnly accountingStartDate, DateOnly today)
    : OperationsDomainException(
        $"Дата операции ({operationDate:yyyy-MM-dd}) должна быть не раньше даты начала учета кошелька " +
        $"({accountingStartDate:yyyy-MM-dd}) и не позже сегодняшнего дня ({today:yyyy-MM-dd} UTC).");
