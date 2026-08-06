namespace LupexWallet.Operations.Domain;

public abstract class OperationsDomainException(string message) : Exception(message);

public sealed class AdjustmentModeRequiredException()
    : OperationsDomainException("Для операции с поведением Adjustment обязателен способ ввода (Absolute/Delta) — решено, Q3.");

public sealed class AdjustmentModeNotAllowedException()
    : OperationsDomainException("Способ ввода корректировки (adjustmentMode) допустим только для операций с поведением Adjustment.");

public sealed class OperationAmountMustBePositiveException()
    : OperationsDomainException("Сумма операции дохода/расхода должна быть положительной — направление определяется поведением типа операции.");

public sealed class DirectOperationCreationNotAllowedForBehaviorException(string behaviorCode)
    : OperationsDomainException($"Операцию с поведением '{behaviorCode}' нельзя создать напрямую через /operations — используйте соответствующий эндпоинт (например, /transfers для Transfer).");

public sealed class OperationPartOfTransferException(Guid operationId)
    : OperationsDomainException($"Операция ({operationId}) — часть перевода; редактируется/удаляется только через перевод целиком (раздел 4 требований).");

public sealed class OperationCurrencyImmutableException()
    : OperationsDomainException("Валюта операции неизменяема после создания (совпадает с валютой кошелька на момент создания).");

public sealed class TransferSameWalletException()
    : OperationsDomainException("Исходный и целевой кошельки перевода должны различаться (раздел 4 требований).");

public sealed class TransferCurrencyMismatchException()
    : OperationsDomainException("Перевод разрешен только между кошельками одной валюты (раздел 4 требований).");

public sealed class TransferDeletionNotAllowedException(Guid transferId)
    : OperationsDomainException($"Перевод ({transferId}) уже учтен в истории баланса и не может быть удален.");

/// <summary>
/// UC-14, решение пользователя от 2026-09-11 (см. ADR-0009, случай b): операцию можно удалить,
/// только если её дата — сегодняшний день; операция с датой строго раньше сегодня считается
/// уже учтенной в истории баланса (после планового пересчета в 12:00 UTC) и удалению не подлежит
/// — только редактирование (UC-13).
/// </summary>
public sealed class OperationDeletionNotAllowedException(Guid operationId)
    : OperationsDomainException($"Операция ({operationId}) уже учтена в истории баланса (дата раньше сегодняшней) и не может быть удалена.");

public sealed class NoActiveOperationTypeForTransferException()
    : OperationsDomainException("Нет ни одного активного типа операции с поведением Transfer — создайте его в справочнике типов операций перед переводом.");

public sealed class OperationTypeReferenceNotFoundException(Guid operationTypeId)
    : OperationsDomainException($"Тип операции ({operationTypeId}) не найден.");

public sealed class OperationTypeReferenceInactiveException(Guid operationTypeId)
    : OperationsDomainException($"Тип операции ({operationTypeId}) деактивирован — нельзя ссылаться на него в новой операции.");

public sealed class WalletReferenceNotFoundException(Guid walletId)
    : OperationsDomainException($"Кошелек ({walletId}) не найден.");

/// <summary>
/// Дата операции/перевода должна лежать в диапазоне [Wallet.AccountingStartDate .. сегодня]
/// (UTC) — те же границы, что уже определяют материализацию истории баланса (Q13:
/// "баланс раньше accounting_start_date равен нулю, снепшоты не материализуются";
/// сегодняшний день — верхняя граница ежедневного слепка, раздел 5 требований). Без этой
/// проверки операция вне диапазона реально меняла бы Wallet.CurrentBalance (синхронно,
/// ADR-0007), но не была бы корректно отражена в истории баланса — расхождение,
/// обнаруженное при реализации BalanceHistory (ADR-0008).
/// </summary>
public sealed class OperationDateOutOfRangeException(DateOnly operationDate, DateOnly accountingStartDate, DateOnly today)
    : OperationsDomainException(
        $"Дата операции ({operationDate:yyyy-MM-dd}) должна быть не раньше даты начала учета кошелька " +
        $"({accountingStartDate:yyyy-MM-dd}) и не позже сегодняшнего дня ({today:yyyy-MM-dd} UTC).");
