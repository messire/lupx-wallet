using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Domain;

/// <summary>События агрегата Wallet (ddd-model.md, §6, раздел "Wallets").</summary>
public sealed record WalletCreated(WalletId WalletId) : DomainEvent;

public sealed record WalletUpdated(WalletId WalletId) : DomainEvent;

public sealed record WalletArchived(WalletId WalletId) : DomainEvent;

public sealed record WalletDeleted(WalletId WalletId) : DomainEvent;

public sealed record PrimaryWalletChanged(WalletId NewPrimaryWalletId, WalletId? PreviousPrimaryWalletId) : DomainEvent;

public sealed record WalletCurrencyChanged(WalletId WalletId, CurrencyId OldCurrencyId, CurrencyId NewCurrencyId) : DomainEvent;

/// <summary>Баланс изменен применением дельты (ADR-0007) — источник дельты (какая
/// операция) в этом событии не указывается: BalanceHistory при реализации будет
/// пересчитывать историю по самим Operation, а не по этому событию.</summary>
public sealed record WalletBalanceChanged(WalletId WalletId, Money Delta) : DomainEvent;
