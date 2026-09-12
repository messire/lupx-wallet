using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Read-only contract published by Wallets for Reporting (Total Amount, UC-19/UC-21).
/// Top-down direction — Reporting.Application references Wallets.Application directly
/// (Reporting is the topmost module, no cycles), so this is not an ADR-0009 case (port
/// owned by the data owner, not the consumer).
/// </summary>
public interface IWalletTotalsSource
{
    /// <summary>
    /// All wallets, including archived (Q16: IncludeInTotal is independent of IsArchived —
    /// the consumer decides inclusion by IncludeInTotal, not archive status).
    /// </summary>
    Task<IReadOnlyList<WalletTotalInfo>> GetAllAsync(CancellationToken cancellationToken);
}

public sealed record WalletTotalInfo(
    WalletId WalletId,
    CurrencyId CurrencyId,
    decimal CurrentBalance,
    bool IncludeInTotal,
    bool IsPrimary);
