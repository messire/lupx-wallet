using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем Wallets для BalanceHistory (ADR-0008) —
/// перечисление всех кошельков, по которым нужно поддерживать ежедневные слепки баланса
/// (плановая задача и досоздание пропусков, ADR-0004). Архивные кошельки включаются —
/// их история баланса не перестает существовать при архивации (ADR-0002: архивация, не
/// удаление).
/// </summary>
public interface IWalletDirectory
{
    Task<IReadOnlyList<WalletId>> GetAllWalletIdsAsync(CancellationToken cancellationToken);
}
