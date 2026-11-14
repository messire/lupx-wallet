namespace LupexWallet.Debts.Domain;

/// <summary>Base of all Debts domain errors; the message goes to Problem.detail as is.</summary>
public abstract class DebtsDomainException(string message) : Exception(message);

/// <summary>Invalid input or reference (HTTP 400).</summary>
public abstract class DebtsValidationException(string message) : DebtsDomainException(message);

/// <summary>Violates the current state of an aggregate (HTTP 409).</summary>
public abstract class DebtsConflictException(string message) : DebtsDomainException(message);

// ---- 400 ----

public sealed class DebtsNameRequiredException() : DebtsValidationException("Название обязательно.");

public sealed class DebtsAmountMustBePositiveException() : DebtsValidationException("Сумма должна быть больше нуля.");

public sealed class CreditorReferenceNotFoundException() : DebtsValidationException("Кредитор не найден.");

public sealed class CreditorReferenceInactiveException()
    : DebtsValidationException("Кредитор деактивирован — нельзя ссылаться на него в новом или изменяемом долге/кредите.");

public sealed class DebtsCurrencyReferenceNotFoundException() : DebtsValidationException("Валюта не найдена среди активных.");

public sealed class PaymentWalletNotFoundException() : DebtsValidationException("Кошелек не найден.");

public sealed class PaymentOperationTypeNotFoundException() : DebtsValidationException("Тип операции не найден.");

public sealed class PaymentOperationTypeInactiveException()
    : DebtsValidationException("Тип операции деактивирован — нельзя использовать его для платежа.");

public sealed class PaymentOperationTypeNotExpenseException()
    : DebtsValidationException("Платёж по долгу или кредиту фиксируется расходом — выберите тип операции с поведением «Расход».");

public sealed class PaymentDateOutOfRangeException(DateOnly date, DateOnly accountingStartDate, DateOnly today)
    : DebtsValidationException(
        $"Дата платежа ({date:yyyy-MM-dd}) должна быть не раньше даты начала учета кошелька " +
        $"({accountingStartDate:yyyy-MM-dd}) и не позже сегодняшнего дня ({today:yyyy-MM-dd} UTC).");

public sealed class LoanScheduleItemAmountMustBePositiveException()
    : DebtsValidationException("Сумма каждого платежа графика должна быть больше нуля.");

public sealed class LoanScheduleGenerationCountOutOfRangeException(int max)
    : DebtsValidationException($"Число платежей графика должно быть от 1 до {max}.");

// ---- 409 ----

public sealed class CreditorInUseException()
    : DebtsConflictException("Кредитор используется в долгах или кредитах — его можно только деактивировать.");

public sealed class DebtNotActiveException()
    : DebtsConflictException("Долг отменён — изменять его и вносить погашения нельзя.");

public sealed class DebtAlreadyCancelledException() : DebtsConflictException("Долг уже отменён.");

public sealed class DebtHasRepaymentsException()
    : DebtsConflictException("По долгу есть погашения — его нельзя удалить, можно только отменить.");

public sealed class LoanNotActiveException()
    : DebtsConflictException("Кредит отменён — изменять его и вносить платежи нельзя.");

public sealed class LoanAlreadyCancelledException() : DebtsConflictException("Кредит уже отменён.");

public sealed class LoanHasPaymentsException()
    : DebtsConflictException("По кредиту есть платежи — его нельзя удалить, можно только отменить.");

public sealed class PaymentCurrencyMismatchException()
    : DebtsConflictException("Валюта кошелька должна совпадать с валютой долга/кредита.");

public sealed class PaymentExceedsRemainingException(decimal remaining)
    : DebtsConflictException($"Сумма платежа больше остатка ({remaining.ToString(System.Globalization.CultureInfo.InvariantCulture)}).");

public sealed class NothingToRepayException() : DebtsConflictException("Остаток равен нулю — погашать нечего.");
