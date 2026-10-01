using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Maps Operation → operations.operations (schema.md, plus the applied_delta_amount column —
/// needed for a correct reversal of the balance effect on Update/Delete, see
/// Operations.Domain.Operation). A balance correction (ADR-0003, §6) is a row of the same table
/// with is_correction = true and stated_balance_amount set; it has no operation type, adjustment
/// mode, transfer or deletion (CHECK constraints below), and at most one exists per wallet and date.
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
        builder.ToTable("operations", table =>
        {
            table.HasCheckConstraint("ck_operations_adjustment_mode", "adjustment_mode IN ('Delta') OR adjustment_mode IS NULL");
            table.HasCheckConstraint("ck_operations_correction_stated_balance", "is_correction = (stated_balance_amount IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_operations_correction_shape",
                "NOT is_correction OR (operation_type_id IS NULL AND adjustment_mode IS NULL AND transfer_id IS NULL AND deleted_at IS NULL)");
            table.HasCheckConstraint("ck_operations_regular_type", "is_correction OR operation_type_id IS NOT NULL");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("wallet_id")
            .IsRequired();

        // NULL only for a balance correction; the CHECK constraints keep regular rows typed.
        builder.Property(x => x.OperationTypeId)
            .HasConversion(id => id == null ? (Guid?)null : id.Value.Value, value => value == null ? null : new OperationTypeId(value.Value))
            .HasColumnName("operation_type_id");

        builder.Property<decimal>("_amount").HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property<decimal>("_appliedDeltaAmount").HasColumnName("applied_delta_amount").HasColumnType("numeric").IsRequired();
        builder.Property<decimal?>("_statedBalanceAmount").HasColumnName("stated_balance_amount").HasColumnType("numeric");

        builder.Property(x => x.IsCorrection).HasColumnName("is_correction").IsRequired();

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

        // Soft delete (ADR-0002, §3): NULL = active. No HasQueryFilter — deleted rows stay visible
        // to lists and to history/usage ports; balance reads exclude them explicitly. It is also the
        // concurrency token: UPDATE is guarded by `deleted_at IS NULL` / `= <loaded value>`, so of two
        // concurrent delete/restore/edit requests only the first succeeds and the balance delta is
        // applied once.
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at").IsConcurrencyToken();

        builder.HasIndex(x => new { x.WalletId, x.OperationDate }).HasDatabaseName("ix_operations_wallet_date");
        builder.HasIndex(x => x.OperationTypeId).HasDatabaseName("ix_operations_operation_type");
        builder.HasIndex(x => x.TransferId).HasDatabaseName("ix_operations_transfer").HasFilter("transfer_id IS NOT NULL");

        // One stated balance per wallet and date; replacing it updates the same row (ADR-0003, §6).
        builder.HasIndex(x => new { x.WalletId, x.OperationDate }, "ux_operations_wallet_correction_date")
            .HasDatabaseName("ux_operations_wallet_correction_date")
            .HasFilter("is_correction")
            .IsUnique();
    }
}
