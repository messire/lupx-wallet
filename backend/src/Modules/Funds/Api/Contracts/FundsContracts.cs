namespace LupexWallet.Funds.Api;

// DTOs of docs/api/openapi.yaml, tag Funds (schemas Fund, FundAllocation, FundCreateRequest,
// FundUpdateRequest, FundAllocationRequest, WalletReservation, FundPage). Amounts are strings (ADR-0005).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record FundAllocationResponse(Guid WalletId, MoneyResponse Amount, MoneyResponse Deficit);

public sealed record FundResponse(
    Guid Id,
    string Name,
    Guid CurrencyId,
    MoneyResponse TargetAmount,
    MoneyResponse Reserved,
    string Progress,
    MoneyResponse Deficit,
    Guid? LinkedDebtId,
    Guid? LinkedLoanId,
    string Status,
    bool HasAllocationHistory,
    IReadOnlyList<FundAllocationResponse> Allocations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record FundPageResponse(IReadOnlyList<FundResponse> Data, CursorPageMeta Pagination);

/// <summary>Amounts are nullable so a missing field is answered with 400, not read as zero.</summary>
public sealed record FundCreateRequest(
    string? Name, decimal? TargetAmount, Guid CurrencyId, Guid? LinkedDebtId, Guid? LinkedLoanId);

/// <summary>PATCH /funds/{id}: all fields are passed in full (like WalletUpdateRequest).</summary>
public sealed record FundUpdateRequest(
    string? Name, decimal? TargetAmount, Guid CurrencyId, Guid? LinkedDebtId, Guid? LinkedLoanId);

public sealed record FundAllocationRequest(Guid WalletId, decimal? Amount);

public sealed record WalletReservationResponse(
    Guid WalletId,
    Guid CurrencyId,
    MoneyResponse Balance,
    MoneyResponse Reserved,
    MoneyResponse Free,
    MoneyResponse Deficit);
