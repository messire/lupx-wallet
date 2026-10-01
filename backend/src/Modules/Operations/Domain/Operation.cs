using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Operation aggregate (ddd-model.md, §2.4). Stores both the user input (Amount +
/// AdjustmentMode — what the user requested) and AppliedDelta — the signed delta actually
/// applied to the wallet balance, so Update/Delete reverse exactly the effect applied before.
/// Deletion is soft only and reversible (ADR-0002, §3).
///
/// A balance correction (<see cref="IsCorrection"/>, ADR-0003, §6) is a system row created when the
/// user states the wallet balance at the end of a day: it carries the stated balance, has no
/// operation type, and its Amount = AppliedDelta is the system-calculated signed amount that makes
/// the balance equal the stated one. The user never edits, moves, soft-deletes or restores it; it
/// is removed only together with its wallet (ADR-0002, §2).
/// </summary>
public sealed class Operation : AggregateRoot<OperationId>
{
    private decimal _amount;
    private decimal _appliedDeltaAmount;
    private decimal? _statedBalanceAmount;

    public WalletId WalletId { get; private set; }

    /// <summary>Null only for a balance correction.</summary>
    public OperationTypeId? OperationTypeId { get; private set; }

    public CurrencyId CurrencyId { get; private set; }
    public DateOnly OperationDate { get; private set; }
    public AdjustmentMode? AdjustmentMode { get; private set; }
    public TransferId? TransferId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Soft-deletion moment (ADR-0002, §3); null while the operation is active.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    /// <summary>System row recording a stated balance and its calculated correction (ADR-0003, §6).</summary>
    public bool IsCorrection { get; private set; }

    /// <summary>The balance the user stated for <see cref="OperationDate"/>; set only for a correction.</summary>
    public Money? StatedBalance => _statedBalanceAmount is { } stated ? new Money(stated, CurrencyId) : null;

    public Money Amount => new(_amount, CurrencyId);

    /// <summary>The signed delta actually applied to the wallet balance.</summary>
    public Money AppliedDelta => new(_appliedDeltaAmount, CurrencyId);

    private Operation()
    {
        // Только для EF Core.
    }

    public static Operation Create(
        OperationId id,
        WalletId walletId,
        OperationTypeId operationTypeId,
        OperationEffectKind effectKind,
        Money amount,
        DateOnly operationDate,
        AdjustmentMode? adjustmentMode,
        Money appliedDelta,
        TransferId? transferId = null)
    {
        ValidateForStandaloneCreation(effectKind, transferId);
        ValidateAmountAndMode(effectKind, amount, adjustmentMode);

        var now = DateTimeOffset.UtcNow;
        var operation = new Operation
        {
            Id = id,
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            CurrencyId = amount.CurrencyId,
            _amount = amount.Amount,
            OperationDate = operationDate,
            AdjustmentMode = adjustmentMode,
            TransferId = transferId,
            _appliedDeltaAmount = appliedDelta.Amount,
            CreatedAt = now,
            UpdatedAt = now,
        };

        operation.Raise(new OperationCreated(id, walletId, operationDate));
        return operation;
    }

    /// <summary>
    /// Creates the correction row for a balance stated for <paramref name="operationDate"/>
    /// (ADR-0003, §6). The amount starts at zero; the caller recalculates it from the wallet journal.
    /// </summary>
    public static Operation CreateCorrection(
        OperationId id, WalletId walletId, CurrencyId currencyId, DateOnly operationDate, decimal statedBalance)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new Operation
        {
            Id = id,
            WalletId = walletId,
            OperationTypeId = null,
            CurrencyId = currencyId,
            _amount = 0m,
            _appliedDeltaAmount = 0m,
            _statedBalanceAmount = statedBalance,
            IsCorrection = true,
            OperationDate = operationDate,
            AdjustmentMode = null,
            TransferId = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        operation.Raise(new OperationCreated(id, walletId, operationDate));
        return operation;
    }

    /// <summary>
    /// Creates an operation that is a leg of a transfer (called only from Transfer, never
    /// directly). Unlike Create, does not check "Transfer cannot be created directly" — this
    /// is that very leg.
    /// </summary>
    internal static Operation CreateForTransfer(
        OperationId id, WalletId walletId, OperationTypeId operationTypeId, Money amount,
        DateOnly operationDate, Money appliedDelta, TransferId transferId)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new Operation
        {
            Id = id,
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            CurrencyId = amount.CurrencyId,
            _amount = amount.Amount,
            OperationDate = operationDate,
            AdjustmentMode = null,
            TransferId = transferId,
            _appliedDeltaAmount = appliedDelta.Amount,
            CreatedAt = now,
            UpdatedAt = now,
        };

        operation.Raise(new OperationCreated(id, walletId, operationDate));
        return operation;
    }

    public void Update(
        OperationTypeId operationTypeId,
        OperationEffectKind effectKind,
        Money amount,
        DateOnly operationDate,
        AdjustmentMode? adjustmentMode,
        Money appliedDelta)
    {
        EnsureEditable();
        ValidateForStandaloneCreation(effectKind, TransferId);
        ValidateAmountAndMode(effectKind, amount, adjustmentMode);

        if (amount.CurrencyId != CurrencyId)
        {
            throw new OperationCurrencyImmutableException();
        }

        ApplyUpdate(WalletId, operationTypeId, amount, operationDate, adjustmentMode, appliedDelta);
    }

    /// <summary>
    /// UC-13: moving an operation to another wallet — equivalent to deleting from the old
    /// wallet and creating on the new one. Operations.Application.UpdateOperationCommand
    /// reverses AppliedDelta on the old wallet and applies the new appliedDelta on the new
    /// wallet via IWalletBalanceGateway around this call (the caller is responsible for both
    /// ApplyDeltaAsync calls). Unlike Update, does not enforce currency immutability — the
    /// operation's currency follows the new wallet's currency (same as primary Create — amount
    /// is already constructed with the new wallet's CurrencyId by the caller).
    /// </summary>
    public void MoveToWallet(
        WalletId newWalletId,
        OperationTypeId operationTypeId,
        OperationEffectKind effectKind,
        Money amount,
        DateOnly operationDate,
        AdjustmentMode? adjustmentMode,
        Money appliedDelta)
    {
        EnsureEditable();
        ValidateForStandaloneCreation(effectKind, TransferId);
        ValidateAmountAndMode(effectKind, amount, adjustmentMode);

        CurrencyId = amount.CurrencyId;
        ApplyUpdate(newWalletId, operationTypeId, amount, operationDate, adjustmentMode, appliedDelta);
    }

    private void ApplyUpdate(
        WalletId newWalletId,
        OperationTypeId operationTypeId,
        Money amount,
        DateOnly operationDate,
        AdjustmentMode? adjustmentMode,
        Money appliedDelta)
    {
        var previousWalletId = WalletId;
        var previousOperationDate = OperationDate;

        WalletId = newWalletId;
        OperationTypeId = operationTypeId;
        _amount = amount.Amount;
        OperationDate = operationDate;
        AdjustmentMode = adjustmentMode;
        _appliedDeltaAmount = appliedDelta.Amount;
        UpdatedAt = DateTimeOffset.UtcNow;

        Raise(new OperationUpdated(Id, WalletId, OperationDate, previousOperationDate, previousWalletId));
    }

    /// <summary>
    /// UC-14, ADR-0002 §3 — soft delete, any date. The row stays; the caller reverses
    /// AppliedDelta on the wallet. UpdatedAt is left untouched (DeletedAt carries the moment).
    /// </summary>
    public void SoftDelete()
    {
        EnsureNotCorrection();
        EnsureNotPartOfTransfer();
        MarkDeleted(DateTimeOffset.UtcNow);
    }

    /// <summary>Revokes soft deletion; the caller re-applies AppliedDelta on the wallet.</summary>
    public void Restore()
    {
        EnsureNotCorrection();
        EnsureNotPartOfTransfer();
        RevokeDeletion();
    }

    /// <summary>Transfer-only entry point (leg deleted together with its transfer, one shared DeletedAt).</summary>
    internal void SoftDeleteAsTransferLeg(DateTimeOffset deletedAt) => MarkDeleted(deletedAt);

    /// <summary>Transfer-only entry point (leg restored together with its transfer).</summary>
    internal void RestoreAsTransferLeg() => RevokeDeletion();

    /// <summary>
    /// Throws if the operation cannot be edited: a correction is checked first (the system
    /// calculates it), then a transfer leg (a leg cannot be restored on its own, so "restore
    /// first" would be misleading), then soft deletion. Lets callers fail before touching other
    /// modules.
    /// </summary>
    public void EnsureEditable()
    {
        EnsureNotCorrection();
        EnsureNotPartOfTransfer();
        EnsureNotDeleted();
    }

    /// <summary>Throws if the operation is soft-deleted.</summary>
    public void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new OperationIsDeletedException(Id.Value);
        }
    }

    /// <summary>
    /// Replaces the stated balance of an existing correction (same row, same id). No change and
    /// no event when the value is the same. The caller then recalculates the amount.
    /// </summary>
    public void ReplaceStatedBalance(Money statedBalance)
    {
        EnsureIsCorrection();

        if (statedBalance.CurrencyId != CurrencyId)
        {
            throw new OperationCurrencyImmutableException();
        }

        if (_statedBalanceAmount == statedBalance.Amount)
        {
            return;
        }

        _statedBalanceAmount = statedBalance.Amount;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new OperationUpdated(Id, WalletId, OperationDate, OperationDate, WalletId));
    }

    /// <summary>
    /// Sets the system-calculated correction amount (Amount = AppliedDelta); does nothing when it
    /// is unchanged. <see cref="CorrectionRecalculated"/> is raised only when the aggregate has no
    /// other unpublished event: a creation or replacement in the same command already reports the
    /// change, so a correction yields a single audit entry per command.
    /// </summary>
    public void ApplyRecalculatedDelta(decimal amount)
    {
        EnsureIsCorrection();

        if (_amount == amount && _appliedDeltaAmount == amount)
        {
            return;
        }

        _amount = amount;
        _appliedDeltaAmount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (DomainEvents.Count == 0)
        {
            Raise(new CorrectionRecalculated(Id, WalletId, OperationDate));
        }
    }

    /// <summary>
    /// The wallet's currency changed: the stated balance and the amount keep their numbers and
    /// follow the wallet's currency. Does nothing when the currency is the same.
    /// </summary>
    public void RebaseCurrency(CurrencyId currencyId)
    {
        EnsureIsCorrection();

        if (CurrencyId == currencyId)
        {
            return;
        }

        CurrencyId = currencyId;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new CorrectionCurrencyChanged(Id, WalletId));
    }

    /// <summary>
    /// The wallet is being deleted: reports the correction as removed. The caller removes the row
    /// physically (a correction is not soft-deletable, ADR-0002, §3).
    /// </summary>
    public void MarkRemovedWithWallet()
    {
        EnsureIsCorrection();
        Raise(new CorrectionRemoved(Id, WalletId, OperationDate));
    }

    private void MarkDeleted(DateTimeOffset deletedAt)
    {
        if (IsDeleted)
        {
            throw new OperationAlreadyDeletedException(Id.Value);
        }

        DeletedAt = deletedAt;
        Raise(new OperationDeleted(Id, WalletId, OperationDate));
    }

    private void RevokeDeletion()
    {
        if (!IsDeleted)
        {
            throw new OperationNotDeletedException(Id.Value);
        }

        DeletedAt = null;
        Raise(new OperationRestored(Id, WalletId, OperationDate));
    }

    private void EnsureNotCorrection()
    {
        if (IsCorrection)
        {
            throw new CorrectionNotEditableException(Id.Value);
        }
    }

    private void EnsureIsCorrection()
    {
        if (!IsCorrection)
        {
            throw new InvalidOperationException("The operation is not a balance correction.");
        }
    }

    private void EnsureNotPartOfTransfer()
    {
        if (TransferId is not null)
        {
            throw new OperationPartOfTransferException(Id.Value);
        }
    }

    private static void ValidateForStandaloneCreation(OperationEffectKind effectKind, TransferId? transferId)
    {
        if (transferId is null && effectKind == OperationEffectKind.Transfer)
        {
            throw new DirectOperationCreationNotAllowedForBehaviorException(nameof(OperationEffectKind.Transfer));
        }
    }

    private static void ValidateAmountAndMode(OperationEffectKind effectKind, Money amount, AdjustmentMode? adjustmentMode)
    {
        if (effectKind == OperationEffectKind.Adjustment)
        {
            if (adjustmentMode is null)
            {
                throw new AdjustmentModeRequiredException();
            }
        }
        else
        {
            if (adjustmentMode is not null)
            {
                throw new AdjustmentModeNotAllowedException();
            }

            if (effectKind != OperationEffectKind.Transfer && amount.Amount <= 0)
            {
                throw new OperationAmountMustBePositiveException();
            }
        }
    }
}
