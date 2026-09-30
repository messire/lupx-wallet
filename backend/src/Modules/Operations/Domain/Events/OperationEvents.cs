using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Operation/Transfer aggregate events (ddd-model.md, §6). The wallet balance effect is
/// required for command correctness and is therefore a synchronous contract, not a
/// subscription to these events (ADR-0001). BalanceHistory is the first real subscriber
/// (ADR-0003): it reacts to OperationCreated/Updated/Deleted/Restored to cascade-recalculate
/// the balance history from the operation date (OperationDate/PreviousOperationDate — the
/// earliest affected date) through today (ADR-0003). Deletion is soft only (ADR-0002, §3):
/// OperationDeleted/TransferDeleted mean "marked deleted", and OperationRestored/
/// TransferRestored mean "deletion revoked"; the row is never physically removed.
/// TransferCreated/TransferDeleted/TransferRestored need no separate subscriber for
/// BalanceHistory: both transfer legs already raise their own
/// OperationCreated/OperationDeleted/OperationRestored (see Operation.CreateForTransfer,
/// Transfer.SoftDelete, Transfer.Restore).
/// </summary>
public sealed record OperationCreated(OperationId OperationId, WalletId WalletId, DateOnly OperationDate) : DomainEvent;

/// <summary>
/// PreviousWalletId — UC-13 (moving an operation to another wallet): equals WalletId when the
/// wallet did not change (plain edit). When it differs, the
/// BalanceHistory.Infrastructure.OperationUpdatedHandler subscriber must recalculate both
/// wallets — the old one (from PreviousOperationDate, where its balance no longer includes
/// this operation) and the new one (from OperationDate, where it starts including it), since
/// each wallet is recalculated independently (IWalletOperationsLookup filters by WalletId).
/// </summary>
public sealed record OperationUpdated(
    OperationId OperationId, WalletId WalletId, DateOnly OperationDate, DateOnly PreviousOperationDate, WalletId PreviousWalletId) : DomainEvent;
public sealed record OperationDeleted(OperationId OperationId, WalletId WalletId, DateOnly OperationDate) : DomainEvent;
public sealed record OperationRestored(OperationId OperationId, WalletId WalletId, DateOnly OperationDate) : DomainEvent;

public sealed record TransferCreated(TransferId TransferId, WalletId SourceWalletId, WalletId TargetWalletId) : DomainEvent;
public sealed record TransferDeleted(TransferId TransferId) : DomainEvent;
public sealed record TransferRestored(TransferId TransferId) : DomainEvent;
