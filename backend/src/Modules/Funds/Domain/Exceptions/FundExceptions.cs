namespace LupexWallet.Funds.Domain;

public abstract class FundDomainException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Validation errors (HTTP 400).</summary>
public abstract class FundValidationException(string message) : FundDomainException(message);

/// <summary>Conflicts with the current state (HTTP 409).</summary>
public abstract class FundConflictException(string message, Exception? innerException = null)
    : FundDomainException(message, innerException);

public sealed class FundNotFoundException() : FundDomainException("Фонд не найден.");

public sealed class FundNameRequiredException() : FundValidationException("Название фонда обязательно.");

public sealed class FundNameTooLongException(int maxLength)
    : FundValidationException($"Название фонда не длиннее {maxLength} символов.");

public sealed class FundTargetAmountMustBePositiveException()
    : FundValidationException("Целевая сумма фонда должна быть больше нуля.");

public sealed class FundLinkAmbiguousException()
    : FundValidationException("Фонд можно связать либо с долгом, либо с кредитом, но не с обоими сразу.");

public sealed class FundCurrencyNotFoundException()
    : FundValidationException("Валюта не найдена среди активных.");

public sealed class FundAmountMustBePositiveException()
    : FundValidationException("Сумма должна быть больше нуля.");

public sealed class FundWalletNotFoundException() : FundValidationException("Кошелек не найден.");

public sealed class FundWalletCurrencyMismatchException()
    : FundValidationException("Валюта кошелька должна совпадать с валютой фонда.");

public sealed class FundWalletArchivedException()
    : FundConflictException("Нельзя резервировать деньги архивного кошелька.");

public sealed class FundInsufficientFreeFundsException(decimal free)
    : FundConflictException(
        $"Недостаточно свободных средств кошелька: доступно {free.ToString(System.Globalization.CultureInfo.InvariantCulture)}.")
{
    public decimal Free { get; } = free;
}

public sealed class FundReleaseExceedsAllocationException(decimal allocated)
    : FundConflictException(
        $"Нельзя снять больше, чем зарезервировано в фонде по этому кошельку: зарезервировано {allocated.ToString(System.Globalization.CultureInfo.InvariantCulture)}.")
{
    public decimal Allocated { get; } = allocated;
}

public sealed class FundClosedException() : FundConflictException("Фонд закрыт — изменить его нельзя.");

public sealed class FundCurrencyChangeNotAllowedException()
    : FundConflictException("Нельзя изменить валюту фонда — по нему уже были резервы.");

public sealed class FundDeletionNotAllowedException()
    : FundConflictException("Нельзя удалить фонд — по нему уже были резервы. Закройте фонд.");

public sealed class FundModifiedConcurrentlyException(Exception innerException)
    : FundConflictException("Фонд изменен параллельно — повторите запрос.", innerException);
