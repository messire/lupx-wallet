using LupexWallet.Funds.Application;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>Implementation of the Funds-owned read port for Reporting (ADR-0010 §7).</summary>
public sealed class FundReservationsLookup(IFundRepository repository) : IFundReservationsLookup
{
    public Task<IReadOnlyList<WalletReservation>> GetReservedByWalletAsync(CancellationToken cancellationToken) =>
        repository.GetReservedByWalletAsync(cancellationToken);
}
