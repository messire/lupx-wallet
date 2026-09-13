using LupexWallet.Operations.Domain;

namespace LupexWallet.Operations.Application;

/// <summary>Maps a ReferenceData behavior code (string) to the local Operations.Domain enum.</summary>
public static class BehaviorKindMapping
{
    public static OperationEffectKind Parse(string behaviorKindCode) => behaviorKindCode switch
    {
        "Income" => OperationEffectKind.Income,
        "Expense" => OperationEffectKind.Expense,
        "Transfer" => OperationEffectKind.Transfer,
        "Adjustment" => OperationEffectKind.Adjustment,
        _ => throw new InvalidOperationException($"Неизвестный код поведения операции: '{behaviorKindCode}'."),
    };
}
