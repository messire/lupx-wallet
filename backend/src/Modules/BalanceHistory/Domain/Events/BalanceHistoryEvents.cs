using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Domain;

/// <summary>
/// BalanceSnapshot aggregate events (ddd-model.md, §6, BalanceHistory command table).
/// Subscriber — Audit: one record per created/updated/deleted snapshot.
/// BalanceSnapshotDeleted (ADR-0002, §2) is raised when snapshots are removed together with
/// their wallet (or rebuilt on a wallet currency change) and carries the value before removal.
/// </summary>
public sealed record BalanceSnapshotCreated(WalletId WalletId, DateOnly SnapshotDate, Money Balance) : DomainEvent;
public sealed record BalanceSnapshotUpdated(WalletId WalletId, DateOnly SnapshotDate, Money OldBalance, Money NewBalance) : DomainEvent;
public sealed record BalanceSnapshotDeleted(WalletId WalletId, DateOnly SnapshotDate, Money Balance) : DomainEvent;
