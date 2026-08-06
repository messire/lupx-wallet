using LupexWallet.BalanceHistory.Domain;

namespace LupexWallet.BalanceHistory.Application;

public sealed record BalanceSnapshotDto(Guid WalletId, DateOnly Date, decimal BalanceAmount, Guid CurrencyId)
{
    public static BalanceSnapshotDto FromDomain(BalanceSnapshot snapshot) =>
        new(snapshot.WalletId.Value, snapshot.SnapshotDate, snapshot.Balance.Amount, snapshot.CurrencyId.Value);
}

public sealed record BalanceSnapshotPageDto(IReadOnlyList<BalanceSnapshotDto> Data, string? NextCursor, bool HasMore);
