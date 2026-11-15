using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Funds.Application;

/// <summary>
/// Builds fund and wallet views with the coverage deficit: reads current wallet balances
/// (IWalletTotalsSource, one query) and the reserves of all active funds per wallet.
/// </summary>
public sealed class FundViewBuilder(IFundRepository repository, IWalletTotalsSource walletTotalsSource)
{
    public async Task<FundDto> BuildAsync(Fund fund, CancellationToken cancellationToken) =>
        (await BuildAsync([fund], cancellationToken))[0];

    public async Task<IReadOnlyList<FundDto>> BuildAsync(IReadOnlyList<Fund> funds, CancellationToken cancellationToken)
    {
        if (funds.Count == 0)
        {
            return [];
        }

        var deficits = await GetWalletDeficitsAsync(cancellationToken);
        return funds.Select(fund => ToDto(fund, deficits)).ToList();
    }

    /// <summary>All wallets, including archived (their reserve is always 0, archiving requires releasing it).</summary>
    public async Task<IReadOnlyList<WalletReservationDto>> BuildWalletSummariesAsync(CancellationToken cancellationToken)
    {
        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        var reserved = await GetReservedAsync(cancellationToken);
        return wallets.Select(w => ToSummary(w.WalletId, w.CurrencyId, w.CurrentBalance, reserved)).ToList();
    }

    public async Task<WalletReservationDto?> BuildWalletSummaryAsync(WalletId walletId, CancellationToken cancellationToken)
    {
        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        var wallet = wallets.FirstOrDefault(w => w.WalletId == walletId);
        if (wallet is null)
        {
            return null;
        }

        var reserved = await repository.GetReservedOnWalletAsync(walletId, cancellationToken);
        return ToSummary(wallet.WalletId, wallet.CurrencyId, wallet.CurrentBalance, new Dictionary<WalletId, decimal> { [walletId] = reserved });
    }

    private static WalletReservationDto ToSummary(
        WalletId walletId, CurrencyId currencyId, decimal balance, IReadOnlyDictionary<WalletId, decimal> reservedByWallet)
    {
        var reserved = reservedByWallet.GetValueOrDefault(walletId);
        return new WalletReservationDto(
            walletId.Value,
            currencyId.Value,
            balance,
            reserved,
            ReservationCoverage.Free(balance, reserved),
            ReservationCoverage.WalletDeficit(balance, reserved));
    }

    private async Task<Dictionary<WalletId, decimal>> GetReservedAsync(CancellationToken cancellationToken) =>
        (await repository.GetReservedByWalletAsync(cancellationToken)).ToDictionary(r => r.WalletId, r => r.Reserved);

    private async Task<Dictionary<WalletId, decimal>> GetWalletDeficitsAsync(CancellationToken cancellationToken)
    {
        var reserved = await GetReservedAsync(cancellationToken);
        if (reserved.Count == 0)
        {
            return [];
        }

        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        return wallets
            .Where(w => reserved.ContainsKey(w.WalletId))
            .ToDictionary(w => w.WalletId, w => ReservationCoverage.WalletDeficit(w.CurrentBalance, reserved[w.WalletId]));
    }

    private static FundDto ToDto(Fund fund, IReadOnlyDictionary<WalletId, decimal> walletDeficits)
    {
        var allocations = fund.Allocations
            .Where(a => a.Amount != 0m)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new FundAllocationDto(
                a.WalletId.Value,
                a.Amount,
                fund.IsActive ? ReservationCoverage.AllocationDeficit(a.Amount, walletDeficits.GetValueOrDefault(a.WalletId)) : 0m))
            .ToList();

        var reserved = fund.Reserved.Amount;
        return new FundDto(
            fund.Id.Value,
            fund.Name,
            fund.CurrencyId.Value,
            fund.TargetAmount,
            reserved,
            reserved / fund.TargetAmount,
            allocations.Sum(a => a.DeficitAmount),
            fund.LinkedDebtId,
            fund.LinkedLoanId,
            fund.Status,
            fund.HasAllocationHistory,
            allocations,
            fund.CreatedAt,
            fund.UpdatedAt,
            fund.ClosedAt);
    }
}
