using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Domain;

/// <summary>Domain events of the Fund aggregate (ADR-0010 §4, §8).</summary>
public sealed record FundCreated(FundId FundId) : DomainEvent;

public sealed record FundUpdated(FundId FundId) : DomainEvent;

public sealed record FundClosed(FundId FundId) : DomainEvent;

public sealed record FundDeleted(FundId FundId) : DomainEvent;

/// <summary>An allocation was created or increased. Amounts are in the fund currency.</summary>
public sealed record FundReserved(
    FundId FundId, WalletId WalletId, CurrencyId CurrencyId, decimal Amount, decimal OldAllocation, decimal NewAllocation) : DomainEvent;

/// <summary>An allocation was decreased: by the user, by closing the fund or by a linked debt/loan payment.</summary>
public sealed record FundReleased(
    FundId FundId,
    WalletId WalletId,
    CurrencyId CurrencyId,
    decimal Amount,
    decimal OldAllocation,
    decimal NewAllocation,
    FundReleaseReason Reason) : DomainEvent;
