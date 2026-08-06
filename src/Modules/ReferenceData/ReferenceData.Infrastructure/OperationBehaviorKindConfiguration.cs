using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Маппинг OperationBehaviorKind → reference_data.operation_behavior_kinds (schema.md).
/// Строки сеются миграцией (HasData) — единственный способ их появления, без CRUD
/// через API/UI (ddd-model.md, §2.3).
/// </summary>
public sealed class OperationBehaviorKindConfiguration : IEntityTypeConfiguration<OperationBehaviorKind>
{
    public void Configure(EntityTypeBuilder<OperationBehaviorKind> builder)
    {
        builder.ToTable("operation_behavior_kinds");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new OperationBehaviorKindId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasData(
            new { Id = OperationBehaviorKind.WellKnownIds.Income, Code = OperationBehaviorKind.Codes.Income, Name = "Доход" },
            new { Id = OperationBehaviorKind.WellKnownIds.Expense, Code = OperationBehaviorKind.Codes.Expense, Name = "Расход" },
            new { Id = OperationBehaviorKind.WellKnownIds.Transfer, Code = OperationBehaviorKind.Codes.Transfer, Name = "Перевод" },
            new { Id = OperationBehaviorKind.WellKnownIds.Adjustment, Code = OperationBehaviorKind.Codes.Adjustment, Name = "Ручная корректировка" });
    }
}
