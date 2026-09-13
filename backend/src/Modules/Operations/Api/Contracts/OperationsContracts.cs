namespace LupexWallet.Operations.Api;

// DTO по контракту docs/api/openapi.yaml (схемы Operation, Transfer, Money, *CreateRequest/*Page).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record OperationResponse(
    Guid Id,
    Guid WalletId,
    Guid OperationTypeId,
    MoneyResponse Amount,
    DateOnly OperationDate,
    string? AdjustmentMode,
    Guid? TransferId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OperationCreateRequest(
    Guid WalletId, Guid OperationTypeId, decimal Amount, DateOnly OperationDate, string? AdjustmentMode);

public sealed record OperationUpdateRequest(
    Guid WalletId, Guid OperationTypeId, decimal Amount, DateOnly OperationDate, string? AdjustmentMode);

public sealed record OperationPageResponse(IReadOnlyList<OperationResponse> Data, CursorPageMeta Pagination);

public sealed record TransferResponse(
    Guid Id,
    Guid SourceWalletId,
    Guid TargetWalletId,
    Guid SourceOperationId,
    Guid TargetOperationId,
    MoneyResponse Amount,
    DateOnly TransferDate,
    DateTimeOffset CreatedAt);

public sealed record TransferCreateRequest(Guid SourceWalletId, Guid TargetWalletId, decimal Amount, DateOnly TransferDate);

public sealed record TransferPageResponse(IReadOnlyList<TransferResponse> Data, CursorPageMeta Pagination);
