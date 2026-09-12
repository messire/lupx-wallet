namespace LupexWallet.BalanceHistory.Api;

// DTO по контракту docs/api/openapi.yaml (схемы BalanceSnapshot, BalanceSnapshotPage, Money).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record BalanceSnapshotResponse(Guid WalletId, DateOnly Date, MoneyResponse Balance);

public sealed record BalanceSnapshotPageResponse(IReadOnlyList<BalanceSnapshotResponse> Data, CursorPageMeta Pagination);
