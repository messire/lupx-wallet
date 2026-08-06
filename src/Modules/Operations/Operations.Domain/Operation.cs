using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Domain;

/// <summary>
/// Агрегат Operation (ddd-model.md, §2.4). Хранит и пользовательский ввод (Amount +
/// AdjustmentMode — что попросил пользователь), и AppliedDelta — фактически применённую
/// к балансу кошелька знаковую дельту (что реально было применено). Раздельное хранение
/// нужно, чтобы Update/Delete могли корректно "откатить" именно тот эффект, который
/// был применён ранее (особенно важно для Adjustment/Absolute, где применённая дельта
/// зависела от баланса кошелька в момент создания и не выводима заново из одного Amount).
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

    /// <summary>Фактически применённая к балансу кошелька знаковая дельта.</summary>
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
    /// Создание операции — части перевода (вызывается только из Transfer, не напрямую).
    /// В отличие от Create, не проверяет "нельзя создать Transfer напрямую" — она и есть
    /// та часть перевода.
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
    /// UC-13, решение пользователя от 2026-09-11: перенос операции на другой кошелек —
    /// эквивалент удаления со старого кошелька + создания на новом. Operations.Application.
    /// UpdateOperationCommand реверсирует AppliedDelta на старом кошельке и применяет новый
    /// appliedDelta на новом кошельке через IWalletBalanceGateway ДО/после вызова этого метода
    /// (вызывающий код отвечает за оба ApplyDeltaAsync). В отличие от Update, не проверяет
    /// неизменность валюты — валюта операции меняется вслед за валютой нового кошелька (та же
    /// логика, что и при первичном Create — amount уже сконструирован с CurrencyId нового
    /// кошелька вызывающим кодом).
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

    /// <summary>
    /// UC-14 — сама история (hasHistory) вычисляется вызывающим кодом (Application),
    /// см. DeleteOperationCommand и решение пользователя от 2026-09-11 (ADR-0009, случай b).
    /// </summary>
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
