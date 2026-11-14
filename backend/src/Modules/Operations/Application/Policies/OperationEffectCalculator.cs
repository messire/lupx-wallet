using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Computes the signed delta an operation should contribute to the wallet balance, and the
/// incremental delta that must actually be applied via IWalletBalanceGateway given what the same
/// operation already applied before (Operation.AppliedDelta) — one formula that works for create,
/// update, and delete (see the comment on Operations.Domain.Operation). A manual adjustment is
/// always a delta; the balance stated for a date is a separate balance correction (ADR-0003, §6).
/// </summary>
public static class OperationEffectCalculator
{
    public static Money ComputeIntendedDelta(OperationEffectKind effectKind, Money amount, AdjustmentMode? mode)
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
            OperationEffectKind.Adjustment => amount,
            _ => throw new InvalidOperationException($"Недостижимая ветка: {effectKind}/{mode}."),
        };
    }

    public static Money ComputeIncrementalDelta(Money intendedDelta, Money previouslyAppliedDelta) =>
        intendedDelta.Subtract(previouslyAppliedDelta);
}
