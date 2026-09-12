using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Computes the intended signed delta an operation should contribute to the wallet balance,
/// and the incremental delta that must actually be applied via IWalletBalanceGateway given
/// what the same operation already applied before (Operation.AppliedDelta) — one formula that
/// works for create, update, and delete (see the comment on Operations.Domain.Operation).
///
/// Known limitation (see ADR-0003): for Adjustment/Absolute, "baseline" is the wallet's
/// current CurrentBalance rather than the fully recomputed history — correct for the current
/// balance, but not a substitute for the full history recalculation added with BalanceHistory.
/// </summary>
public static class OperationEffectCalculator
{
    public static Money ComputeIntendedDelta(
        OperationEffectKind effectKind, Money amount, AdjustmentMode? mode, Money baselineBalance)
    {
        // Валидация здесь (а не только в Operation.Create/Update) — калькулятор вызывается
        // ДО конструирования/мутации агрегата в обработчиках команд (нужно знать intendedDelta
        // заранее, чтобы применить её через IWalletBalanceGateway). Без этих проверок
        // невалидная комбинация приводила бы к обычному InvalidOperationException вместо
        // доменного исключения, которое API-слой умеет превращать в 400/409 (нашлось при
        // тестировании: попытка отредактировать операцию с behaviorKind=Transfer роняла 500).
        if (effectKind == OperationEffectKind.Transfer)
        {
            throw new DirectOperationCreationNotAllowedForBehaviorException(nameof(OperationEffectKind.Transfer));
        }

        if (effectKind == OperationEffectKind.Adjustment && mode is null)
        {
            throw new AdjustmentModeRequiredException();
        }

        if (effectKind != OperationEffectKind.Adjustment && mode is not null)
        {
            throw new AdjustmentModeNotAllowedException();
        }

        return effectKind switch
        {
            OperationEffectKind.Income => amount,
            OperationEffectKind.Expense => amount.Negate(),
            OperationEffectKind.Adjustment when mode == AdjustmentMode.Delta => amount,
            OperationEffectKind.Adjustment when mode == AdjustmentMode.Absolute => amount.Subtract(baselineBalance),
            _ => throw new InvalidOperationException($"Недостижимая ветка: {effectKind}/{mode}."),
        };
    }

    /// <summary>baseline = current balance minus what this same operation already contributed before (0 on creation).</summary>
    public static Money ComputeBaseline(Money currentBalance, Money previouslyAppliedDelta) =>
        currentBalance.Subtract(previouslyAppliedDelta);

    public static Money ComputeIncrementalDelta(Money intendedDelta, Money previouslyAppliedDelta) =>
        intendedDelta.Subtract(previouslyAppliedDelta);
}
