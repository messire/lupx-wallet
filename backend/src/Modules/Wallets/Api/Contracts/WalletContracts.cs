namespace LupexWallet.Wallets.Api;

// DTO по контракту docs/api/openapi.yaml (схемы Wallet, WalletCreateRequest, WalletPage, Money).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record WalletResponse(
    Guid Id,
    string Name,
    Guid WalletTypeId,
    string? PurposeDescription,
    Guid CurrencyId,
    MoneyResponse InitialBalance,
    DateOnly AccountingStartDate,
    MoneyResponse CurrentBalance,
    bool IncludeInTotal,
    bool IsPrimary,
    bool IsArchived,
    int DisplayOrder,
    string? Color,
    string? Icon,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WalletCreateRequest(
    string Name,
    Guid WalletTypeId,
    Guid CurrencyId,
    decimal InitialBalanceAmount,
    DateOnly AccountingStartDate,
    string? PurposeDescription,
    bool? IncludeInTotal,
    int? DisplayOrder,
    string? Color,
    string? Icon);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record WalletPageResponse(IReadOnlyList<WalletResponse> Data, CursorPageMeta Pagination);

/// <summary>UC-02 (PATCH /wallets/{id}) — not a partial merge despite PATCH: all fields are passed in full (see UpdateWalletCommand). IncludeInTotal is ignored (stays true) for the primary wallet (Q8).</summary>
public sealed record WalletUpdateRequest(
    string Name,
    Guid WalletTypeId,
    string? PurposeDescription,
    bool IncludeInTotal,
    int DisplayOrder,
    string? Color,
    string? Icon);

/// <summary>UC-06 (PUT /wallets/{id}/currency) — Q15.</summary>
public sealed record WalletChangeCurrencyRequest(Guid CurrencyId);
