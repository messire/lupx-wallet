namespace LupexWallet.Wallets.Domain;

public abstract class WalletDomainException(string message) : Exception(message);

public sealed class WalletNameRequiredException() : WalletDomainException("Название кошелька обязательно.");

public sealed class WalletCurrencyMismatchException()
    : WalletDomainException("Валюта начального баланса должна совпадать с валютой кошелька.");

public sealed class CannotArchivePrimaryWalletException(Guid walletId)
    : WalletDomainException($"Нельзя архивировать основной кошелек ({walletId}) — сначала назначьте другой основным.");

public sealed class CannotSetArchivedWalletAsPrimaryException(Guid walletId)
    : WalletDomainException($"Нельзя назначить архивный кошелек ({walletId}) основным.");

public sealed class WalletCurrencyChangeNotAllowedException(Guid walletId)
    : WalletDomainException($"Нельзя изменить валюту кошелька ({walletId}) — по нему уже есть история (операции/слепки).");

public sealed class WalletDeletionNotAllowedException(Guid walletId)
    : WalletDomainException($"Нельзя удалить кошелек ({walletId}) — по нему уже есть история. Используйте архивирование.");
