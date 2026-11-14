namespace LupexWallet.Planning.Domain;

/// <summary>Base of Planning errors; the message goes to Problem.detail as is. Plain subclasses map to 400.</summary>
public abstract class PlanningDomainException(string message) : Exception(message);

/// <summary>Breaks a lifecycle or state invariant; maps to 409.</summary>
public abstract class PlanningConflictException(string message) : PlanningDomainException(message);

/// <summary>Maps to 404.</summary>
public abstract class PlanningNotFoundException(string message) : PlanningDomainException(message);

// ---- 404 ----

public sealed class PlannedTransactionNotFoundException() : PlanningNotFoundException("Плановая операция не найдена.");

public sealed class PlannedOccurrenceNotFoundException(DateOnly occurrenceDate)
    : PlanningNotFoundException($"У плановой операции нет наступления на дату {occurrenceDate:yyyy-MM-dd}.");

// ---- 400 ----

public sealed class RecurrenceRuleInvalidException(string message) : PlanningDomainException(message);

public sealed class PlannedAmountMustBePositiveException()
    : PlanningDomainException("Сумма плановой операции должна быть положительной — направление определяется типом операции.");

public sealed class PlannedDescriptionTooLongException(int maxLength)
    : PlanningDomainException($"Описание плановой операции — не длиннее {maxLength} символов.");

public sealed class RecurrenceUntilBeforeStartException()
    : PlanningDomainException("Дата окончания повторения не может быть раньше даты первого наступления.");

public sealed class PlannedOperationTypeNotFoundException() : PlanningDomainException("Тип операции не найден.");

public sealed class PlannedOperationTypeInactiveException()
    : PlanningDomainException("Тип операции деактивирован — выберите активный тип.");

public sealed class PlannedOperationTypeBehaviorNotSupportedException()
    : PlanningDomainException("Плановой может быть только операция дохода или расхода.");

public sealed class PlannedCurrencyNotFoundException() : PlanningDomainException("Валюта не найдена среди активных.");

public sealed class PlannedWalletNotFoundException() : PlanningDomainException("Кошелёк не найден.");

public sealed class PlannedWalletCurrencyMismatchException()
    : PlanningDomainException("Валюта кошелька должна совпадать с валютой плановой операции.");

public sealed class PlannedExecutionWalletRequiredException()
    : PlanningDomainException("Укажите кошелёк, из которого исполняется плановая операция.");

public sealed class PlannedExecutionDateOutOfRangeException(DateOnly date, DateOnly accountingStartDate, DateOnly today)
    : PlanningDomainException(
        $"Дата операции ({date:yyyy-MM-dd}) должна быть не раньше даты начала учёта кошелька ({accountingStartDate:yyyy-MM-dd}) " +
        $"и не позже сегодняшнего дня ({today:yyyy-MM-dd} UTC).");

public sealed class PlannedOccurrenceRangeInvalidException(string message) : PlanningDomainException(message);

/// <summary>A request field is missing or has an unknown value (e.g. an enum name).</summary>
public sealed class PlanningRequestInvalidException(string message) : PlanningDomainException(message);

// ---- 409 ----

public sealed class PlannedTransactionFinishedException()
    : PlanningConflictException("Плановая операция завершена — изменять её и её наступления нельзя.");

public sealed class PlannedScheduleChangeConflictException()
    : PlanningConflictException(
        "Нельзя изменить расписание: по наступлению, которого не будет в новом расписании, уже есть исполнение. " +
        "Завершите плановую операцию и создайте новую.");

public sealed class PlannedCurrencyChangeNotAllowedException()
    : PlanningConflictException("Нельзя изменить валюту — у плановой операции уже есть исполнения.");

public sealed class PlannedDirectionChangeNotAllowedException()
    : PlanningConflictException("Нельзя сменить доход на расход или наоборот — у плановой операции уже есть исполнения.");

public sealed class PlannedTransactionHasExecutionsException()
    : PlanningConflictException("У плановой операции есть исполнения — её можно только завершить.");

public sealed class PlannedOccurrenceAlreadyExecutedException()
    : PlanningConflictException("Наступление уже исполнено полностью.");

public sealed class PlannedOccurrenceSkippedException()
    : PlanningConflictException("Наступление пропущено — сначала отмените пропуск.");

public sealed class PlannedOccurrenceNotSkippedException()
    : PlanningConflictException("Наступление не пропущено — отменять нечего.");

/// <summary>The plan's default wallet changed while the execution waited for its lock (review v0.4.0, R7).</summary>
public sealed class PlannedTransactionModifiedConcurrentlyException()
    : PlanningConflictException("Плановая операция изменена параллельно — обновите данные и повторите.");
