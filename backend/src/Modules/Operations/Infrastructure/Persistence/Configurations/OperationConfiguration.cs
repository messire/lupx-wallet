using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Maps Operation → operations.operations (schema.md, plus the applied_delta_amount column —
/// needed for a correct reversal of the balance effect on Update/Delete, see
/// Operations.Domain.Operation).
///
/// transfer_id is a plain column with no EF-level relationship (no .HasOne/.WithMany): the FK
/// to operations.transfers is added as raw SQL in the migration as DEFERRABLE INITIALLY
/// DEFERRED, to avoid a cyclic dependency when inserting a transfer (Transfer and its 2
/// Operation rows are created in one transaction, referencing each other).
/// </summary>
public sealed class OperationConfiguration : IEntityTypeConfiguration<Operation>
{
    public void Configure(EntityTypeBuilder<Operation> builder)
    {
        builder.ToTable("operations", table => table.HasCheckConstraint(
            "ck_operations_adjustment_mode", "adjustment_mode IN ('Absolute', 'Delta') OR adjustment_mode IS NULL"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("wallet_id")
            .IsRequired();

        builder.Property(x => x.OperationTypeId)
            .HasConversion(id => id.Value, value => new OperationTypeId(value))
            .HasColumnName("operation_type_id")
            .IsRequired();

        builder.Property<decimal>("_amount").HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property<decimal>("_appliedDeltaAmount").HasColumnName("applied_delta_amount").HasColumnType("numeric").IsRequired();

        builder.Property(x => x.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property(x => x.OperationDate).HasColumnName("operation_date").IsRequired();

        builder.Property(x => x.AdjustmentMode)
            .HasConversion(v => v == null ? null : v.ToString(), v => v == null ? null : Enum.Parse<AdjustmentMode>(v))
            .HasColumnName("adjustment_mode")
            .HasMaxLength(16);

        builder.Property(x => x.TransferId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value.Value, value => value == null ? null : new TransferId(value.Value))
            .HasColumnName("transfer_id");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.WalletId, x.OperationDate }).HasDatabaseName("ix_operations_wallet_date");
        builder.HasIndex(x => x.OperationTypeId).HasDatabaseName("ix_operations_operation_type");
        builder.HasIndex(x => x.TransferId).HasDatabaseName("ix_operations_transfer").HasFilter("transfer_id IS NOT NULL");
    }
}
