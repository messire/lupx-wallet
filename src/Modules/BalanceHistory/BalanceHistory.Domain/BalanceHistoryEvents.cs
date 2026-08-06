using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Domain;

/// <summary>
/// События агрегата BalanceSnapshot (ddd-model.md, §6, таблица команд BalanceHistory).
/// Подписчик — Audit (модуль-заглушка на момент реализации BalanceHistory): по одной
/// записи на каждый созданный/обновленный слепок.
/// </summary>
public sealed record BalanceSnapshotCreated(WalletId WalletId, DateOnly SnapshotDate, Money Balance) : DomainEvent;
public sealed record BalanceSnapshotUpdated(WalletId WalletId, DateOnly SnapshotDate, Money OldBalance, Money NewBalance) : DomainEvent;
