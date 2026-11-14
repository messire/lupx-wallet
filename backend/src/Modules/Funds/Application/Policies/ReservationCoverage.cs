namespace LupexWallet.Funds.Application;

/// <summary>
/// Free funds and the reserve coverage deficit (ADR-0010 §4, B7; §7). Expenses are never
/// blocked, so the balance may drop below the reserves; the deficit is the uncovered part of the reserves.
/// </summary>
public static class ReservationCoverage
{
    /// <summary>Free funds = balance − reserved; negative when the balance is below the reserves.</summary>
    public static decimal Free(decimal balance, decimal reserved) => balance - reserved;

    /// <summary>
    /// Wallet deficit = reserved − balance when positive, capped at the reserves: a negative balance
    /// cannot leave more than the whole reserve uncovered.
    /// </summary>
    public static decimal WalletDeficit(decimal balance, decimal reserved) =>
        Math.Clamp(reserved - balance, 0m, Math.Max(reserved, 0m));

    /// <summary>
    /// Deficit of one fund's allocation on the wallet: the wallet deficit, capped at the allocation.
    /// Each fund sharing the wallet shows the part of the shortage it may be affected by.
    /// </summary>
    public static decimal AllocationDeficit(decimal allocation, decimal walletDeficit) =>
        Math.Min(allocation, walletDeficit);
}
