using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// System reference (ddd-model.md, §2.3) — a fixed set of four base operation behavior
/// kinds. Unlike WalletType/OperationType/Currency, it is not user-editable: rows are
/// seeded by a migration at deployment, so the class does not extend AggregateRoot (no
/// domain commands/events could change it) — it is just a data container materialized by
/// EF Core.
/// </summary>
public sealed class OperationBehaviorKind
{
    public OperationBehaviorKindId Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    private OperationBehaviorKind()
    {
        // Только для EF Core.
    }

    public static class Codes
    {
        public const string Income = "Income";
        public const string Expense = "Expense";
        public const string Transfer = "Transfer";
        public const string Adjustment = "Adjustment";
    }

    /// <summary>
    /// Fixed Ids for seed data (migration) — the only place these GUIDs are defined;
    /// Infrastructure references them instead of duplicating the values.
    /// </summary>
    public static class WellKnownIds
    {
        public static readonly OperationBehaviorKindId Income = new(new Guid("24064f7c-82b3-4407-a1bb-b3706c0643c1"));
        public static readonly OperationBehaviorKindId Expense = new(new Guid("d042ac13-0644-4ded-8fdd-518e6035b107"));
        public static readonly OperationBehaviorKindId Transfer = new(new Guid("107ca9ff-3b38-4a58-85d7-236703f49a46"));
        public static readonly OperationBehaviorKindId Adjustment = new(new Guid("322fb879-0c8e-464e-bca1-9e84964ad232"));
    }
}
