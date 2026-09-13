namespace LupexWallet.Wallets.Domain;

public abstract class WalletDomainException(string message) : Exception(message);

public sealed class WalletNameRequiredException() : WalletDomainException("Название кошелька обязательно.");

public sealed class WalletCurrencyMismatchException()
    : WalletDomainException("Валюта начального баланса должна совпадать с валютой кошелька.");

public sealed class CannotArchivePrimaryWalletException(Guid walletId)
    : WalletDomainException($"Нельзя архивировать основной кошелек ({walletId}) — сначала назначьте другой основным.");

public sealed class CannotSetArchivedWalletAsPrimaryException(Guid walletId)
    : WalletDomainException($"Нельзя назначить архивный кошелек ({walletId}) основным.");

public sealed class CannotDeletePrimaryWalletException(Guid walletId)
    : WalletDomainException($"Нельзя удалить основной кошелек ({walletId}) — сначала назначьте другой основным.");

public sealed class WalletCurrencyChangeNotAllowedException(Guid walletId)
    : WalletDomainException($"Нельзя изменить валюту кошелька ({walletId}) — по нему уже есть история (операции/слепки).");

public sealed class WalletDeletionNotAllowedException(Guid walletId)
    : WalletDomainException($"Нельзя удалить кошелек ({walletId}) — по нему уже есть история. Используйте архивирование.");

public sealed class WalletNotFoundException(Guid walletId)
    : WalletDomainException($"Кошелек ({walletId}) не найден.");

/// <summary>W2.5, case d: reference validation on wallet create/update, mirroring
/// Operations.Domain.WalletReferenceNotFoundException/OperationTypeReferenceNotFoundException.</summary>
public sealed class WalletTypeReferenceNotFoundException(Guid walletTypeId)
    : WalletDomainException($"Тип кошелька ({walletTypeId}) не найден.");

public sealed class WalletTypeReferenceInactiveException(Guid walletTypeId)
    : WalletDomainException($"Тип кошелька ({walletTypeId}) деактивирован — нельзя ссылаться на него в новом/изменяемом кошельке.");

public sealed class CurrencyReferenceNotFoundException(Guid currencyId)
    : WalletDomainException($"Валюта ({currencyId}) не найдена.");

public sealed class CurrencyReferenceInactiveException(Guid currencyId)
    : WalletDomainException($"Валюта ({currencyId}) деактивирована — нельзя ссылаться на неё при создании кошелька.");
