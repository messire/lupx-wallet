namespace LupexWallet.ReferenceData.Api;

// DTO по контракту docs/api/openapi.yaml (схемы ReferenceItem, OperationType, Currency,
// OperationBehaviorKind и их *CreateRequest/*Page варианты).

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record ReferenceItemResponse(Guid Id, string Name, bool IsActive);
public sealed record ReferenceItemCreateRequest(string Name);
public sealed record ReferenceItemPageResponse(IReadOnlyList<ReferenceItemResponse> Data, CursorPageMeta Pagination);

public sealed record OperationTypeResponse(Guid Id, string Name, Guid BehaviorKindId, bool IsActive);
public sealed record OperationTypeCreateRequest(string Name, Guid BehaviorKindId);
public sealed record OperationTypePageResponse(IReadOnlyList<OperationTypeResponse> Data, CursorPageMeta Pagination);

public sealed record CurrencyResponse(Guid Id, string Code, string Name, bool IsActive);
public sealed record CurrencyCreateRequest(string Code, string Name);
public sealed record CurrencyPageResponse(IReadOnlyList<CurrencyResponse> Data, CursorPageMeta Pagination);

public sealed record OperationBehaviorKindResponse(Guid Id, string Code, string Name);
