using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Operation aggregate (ddd-model.md, §2.4). Stores both the user input (Amount +
/// AdjustmentMode — what the user requested) and AppliedDelta — the signed delta actually
/// applied to the wallet balance. Keeping them separate lets Update/Delete correctly "undo"
/// exactly the effect previously applied (especially important for Adjustment/Absolute,
/// where the applied delta depended on the wallet balance at creation time and cannot be
/// re-derived from Amount alone).
/// </summary>
public sealed class Operation : AggregateRoot<OperationId>
{
    private decimal _amount;
    private decimal _appliedDeltaAmount;

    public WalletId WalletId { get; private set; }
    public OperationTypeId OperationTypeId { get; private set; }
    public CurrencyId CurrencyId { get; private set; }
    public DateOnly OperationDate { get; private set; }
    public AdjustmentMode? AdjustmentMode { get; private set; }
    public TransferId? TransferId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

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
        EnsureNotPartOfTransfer();
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
        EnsureNotPartOfTransfer();
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

    /// <summary>UC-14 — hasHistory is computed by the caller (Application); see DeleteOperationCommand and ADR-0009, case b.</summary>
    public void EnsureCanBeDeleted(bool hasHistory)
    {
        EnsureNotPartOfTransfer();

        if (hasHistory)
        {
            throw new OperationDeletionNotAllowedException(Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new OperationDeleted(Id, WalletId, OperationDate));

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
