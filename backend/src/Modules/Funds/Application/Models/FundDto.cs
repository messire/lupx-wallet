using LupexWallet.Funds.Domain;

namespace LupexWallet.Funds.Application;

/// <summary>Fund with reserve, progress and coverage deficit (ADR-0010 §4).</summary>
public sealed record FundDto(
    Guid Id,
    string Name,
    Guid CurrencyId,
    decimal TargetAmount,
    decimal ReservedAmount,
    decimal Progress,
    decimal DeficitAmount,
    Guid? LinkedDebtId,
    Guid? LinkedLoanId,
    FundStatus Status,
    bool HasAllocationHistory,
    IReadOnlyList<FundAllocationDto> Allocations,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt);

/// <summary>Non-zero allocation of a fund on one wallet.</summary>
public sealed record FundAllocationDto(Guid WalletId, decimal Amount, decimal DeficitAmount);

public sealed record FundPageDto(IReadOnlyList<FundDto> Data, string? NextCursor, bool HasMore);

/// <summary>Per-wallet reservation summary (ADR-0010 §4, §7): balance, reserved, free, deficit.</summary>
public sealed record WalletReservationDto(
    Guid WalletId,
    Guid CurrencyId,
    decimal BalanceAmount,
    decimal ReservedAmount,
    decimal FreeAmount,
    decimal DeficitAmount);
