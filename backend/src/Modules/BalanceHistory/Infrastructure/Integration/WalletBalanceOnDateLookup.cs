using LupexWallet.BalanceHistory.Application;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Implementation of IWalletBalanceOnDateLookup (Reporting, W2.1) — the same algorithm as
/// GetWalletBalanceQueryHandler (BalanceHistoryQueries.cs) for the "date given" branch;
/// duplicated rather than extracted into a shared method, to avoid coupling two different
/// entry points (an HTTP request for a specific wallet vs. Reporting's aggregation across
/// all wallets) for the sake of a few lines.
/// </summary>
public sealed class WalletBalanceOnDateLookup(
    IWalletBalanceGateway walletGateway,
    IBalanceSnapshotRepository repository,
    IWalletOperationsLookup operationsLookup) : IWalletBalanceOnDateLookup
{
    public async Task<decimal?> GetBalanceOnDateAsync(WalletId walletId, DateOnly date, CancellationToken cancellationToken)
    {
        var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null)
        {
            return null;
        }

        if (date < wallet.AccountingStartDate)
        {
            // Решено, Q13: баланс раньше accounting_start_date всегда равен нулю.
            return 0m;
        }

        var snapshot = await repository.GetAsync(walletId, date, cancellationToken);
        if (snapshot is not null)
        {
            return snapshot.Balance.Amount;
        }

        // Слепок на дату еще не материализован — считаем на лету, не сохраняя результат
        // (материализация — забота BalanceRecalculationService/плановой задачи, ADR-0004).
        var deltas = await operationsLookup.GetDailyDeltasUpToAsync(walletId, date, cancellationToken);
        return wallet.InitialBalance.Amount + deltas.Sum(d => d.DeltaAmount);
    }
}
