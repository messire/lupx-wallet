using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Узкий контракт, публикуемый модулем Wallets для модулей Operations (ADR-0007) и
/// BalanceHistory (ADR-0008) — синхронное чтение и изменение баланса. Для Operations
/// эффект обязателен для корректности команд создания/изменения/удаления операции и
/// перевода. Для BalanceHistory используется только чтение (InitialBalance,
/// AccountingStartDate) — нужны для восстановления баланса на произвольную дату при
/// каскадном пересчете (ADR-0003). Работает через тот же WalletsDbContext/SaveChangesAsync,
/// поэтому участвует в общем TransactionScope команды без дополнительной оркестрации.
/// </summary>
public interface IWalletBalanceGateway
{
    Task<WalletBalanceInfo?> GetAsync(WalletId id, CancellationToken cancellationToken);

    /// <summary>Применяет знаковую дельту к CurrentBalance (положительную или отрицательную).</summary>
    Task ApplyDeltaAsync(WalletId id, Money delta, CancellationToken cancellationToken);
}

public sealed record WalletBalanceInfo(
    WalletId Id,
    CurrencyId CurrencyId,
    Money InitialBalance,
    Money CurrentBalance,
    DateOnly AccountingStartDate,
    bool IsArchived);
