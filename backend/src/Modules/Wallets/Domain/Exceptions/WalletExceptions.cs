namespace LupexWallet.Wallets.Domain;

public abstract class WalletDomainException(string message) : Exception(message);

public sealed class WalletNameRequiredException() : WalletDomainException("Название кошелька обязательно.");

public sealed class WalletCurrencyMismatchException()
    : WalletDomainException("Валюта начального баланса должна совпадать с валютой кошелька.");

public sealed class CannotArchivePrimaryWalletException()
    : WalletDomainException("Нельзя архивировать основной кошелек — сначала назначьте другой основным.");

public sealed class CannotSetArchivedWalletAsPrimaryException()
    : WalletDomainException("Нельзя назначить архивный кошелек основным.");

public sealed class CannotDeletePrimaryWalletException()
    : WalletDomainException("Нельзя удалить основной кошелек — сначала назначьте другой основным.");

public sealed class WalletCurrencyChangeNotAllowedException()
    : WalletDomainException("Нельзя изменить валюту кошелька — по нему уже есть операции (включая удалённые).");

public sealed class WalletDeletionNotAllowedException()
    : WalletDomainException("Нельзя удалить кошелек — у кошелька есть операции (включая удалённые). Используйте архивирование.");

public sealed class WalletNotFoundException() : WalletDomainException("Кошелек не найден.");

/// <summary>Reference validation on wallet create/update, mirroring
/// Operations.Domain.WalletReferenceNotFoundException/OperationTypeReferenceNotFoundException.</summary>
public sealed class WalletTypeReferenceNotFoundException() : WalletDomainException("Тип кошелька не найден.");

public sealed class WalletTypeReferenceInactiveException()
    : WalletDomainException("Тип кошелька деактивирован — нельзя ссылаться на него в новом/изменяемом кошельке.");

/// <summary>The currency is neither active nor (for creation by code) available.</summary>
public sealed class CurrencyReferenceNotFoundException() : WalletDomainException("Валюта не найдена или недоступна.");
