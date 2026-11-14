using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Application;

/// <summary>
/// Read-only port published by Funds for Reporting (ADR-0010 §7, free funds and total amount;
/// ADR-0001 §2, way c: port owned by the data owner). Reserved amount per wallet over active funds.
/// </summary>
public interface IFundReservationsLookup
{
    /// <summary>Wallets with a non-zero reserve of active funds; amounts are in the wallet (= fund) currency.</summary>
    Task<IReadOnlyList<WalletReservation>> GetReservedByWalletAsync(CancellationToken cancellationToken);
}

public sealed record WalletReservation(WalletId WalletId, CurrencyId CurrencyId, decimal Reserved);
