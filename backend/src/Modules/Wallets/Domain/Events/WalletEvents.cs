using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Domain;

/// <summary>Domain events of the Wallet aggregate (ddd-model.md, §6, "Wallets").</summary>
public sealed record WalletCreated(WalletId WalletId) : DomainEvent;

public sealed record WalletUpdated(WalletId WalletId) : DomainEvent;

public sealed record WalletArchived(WalletId WalletId) : DomainEvent;

public sealed record WalletDeleted(WalletId WalletId) : DomainEvent;

public sealed record PrimaryWalletChanged(WalletId NewPrimaryWalletId, WalletId? PreviousPrimaryWalletId) : DomainEvent;

public sealed record WalletCurrencyChanged(WalletId WalletId, CurrencyId OldCurrencyId, CurrencyId NewCurrencyId) : DomainEvent;

/// <summary>Balance changed by applying a delta (ADR-0007); the source operation is not
/// included — BalanceHistory recalculates from Operation records, not from this event.</summary>
public sealed record WalletBalanceChanged(WalletId WalletId, Money Delta) : DomainEvent;
