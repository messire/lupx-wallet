using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Consumer-owned port (ADR-0001 §2, way d; ADR-0010 §4): "does this wallet hold a reserve of an
/// active fund". A wallet with a reserve cannot be archived, deleted or change its currency until
/// the reserve is released. Exactly one implementation is expected:
/// Funds.Infrastructure.FundsWalletReservationProbe (AddFundsModule). An empty implementation
/// list is a DI configuration error, not "no reserve".
/// </summary>
public interface IWalletReservationProbe
{
    Task<bool> HasActiveReservationsAsync(WalletId walletId, CancellationToken cancellationToken);
}

/// <summary>Aggregation over all registered probes with the fail-fast empty-list check.</summary>
public static class WalletReservationProbeExtensions
{
    public static async Task<bool> AnyHasActiveReservationsAsync(
        this IEnumerable<IWalletReservationProbe> probes, WalletId walletId, CancellationToken cancellationToken)
    {
        var materializedProbes = probes as ICollection<IWalletReservationProbe> ?? probes.ToList();
        if (materializedProbes.Count == 0)
        {
            throw new InvalidOperationException(
                "Не зарегистрировано ни одной реализации IWalletReservationProbe — ожидалась " +
                "Funds.Infrastructure.FundsWalletReservationProbe (AddFundsModule). Пустой список — " +
                "ошибка конфигурации DI, не «резервов нет» (ADR-0001 §2).");
        }

        // Sequentially: the probes share one connection inside the command's TransactionScope.
        foreach (var probe in materializedProbes)
        {
            if (await probe.HasActiveReservationsAsync(walletId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
