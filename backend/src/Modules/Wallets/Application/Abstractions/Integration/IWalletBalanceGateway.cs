using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Narrow contract published by Wallets for Operations (ADR-0007) and BalanceHistory
/// (ADR-0008) — synchronous balance read/write. Operations needs the write effect for
/// create/update/delete/transfer commands; BalanceHistory only reads InitialBalance and
/// AccountingStartDate for cascading recalculation (ADR-0003). Shares WalletsDbContext /
/// SaveChangesAsync, so it participates in the command's ambient TransactionScope.
/// </summary>
public interface IWalletBalanceGateway
{
    Task<WalletBalanceInfo?> GetAsync(WalletId id, CancellationToken cancellationToken);

    /// <summary>Applies a signed delta (positive or negative) to CurrentBalance.</summary>
    Task ApplyDeltaAsync(WalletId id, Money delta, CancellationToken cancellationToken);
}

public sealed record WalletBalanceInfo(
    WalletId Id,
    CurrencyId CurrencyId,
    Money InitialBalance,
    Money CurrentBalance,
    DateOnly AccountingStartDate,
    bool IsArchived);
