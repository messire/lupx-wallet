using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Domain;

/// <summary>
/// BalanceSnapshot aggregate events (ddd-model.md, §6, BalanceHistory command table).
/// Subscriber — Audit (a stub module at the time BalanceHistory was implemented): one
/// record per created/updated snapshot.
/// </summary>
public sealed record BalanceSnapshotCreated(WalletId WalletId, DateOnly SnapshotDate, Money Balance) : DomainEvent;
public sealed record BalanceSnapshotUpdated(WalletId WalletId, DateOnly SnapshotDate, Money OldBalance, Money NewBalance) : DomainEvent;
