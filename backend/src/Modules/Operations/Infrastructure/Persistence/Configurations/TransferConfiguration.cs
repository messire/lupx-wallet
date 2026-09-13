using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Maps Transfer → operations.transfers (schema.md). source_operation_id/target_operation_id
/// are plain columns with no EF-level relationship; the FK to operations.operations and their
/// UNIQUE constraints are added as raw SQL in the migration (see OperationConfiguration — same
/// cyclic dependency).
/// </summary>
public sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> builder)
    {
        builder.ToTable("transfers", table =>
        {
            table.HasCheckConstraint("ck_transfers_distinct_wallets", "source_wallet_id <> target_wallet_id");
            table.HasCheckConstraint("ck_transfers_amount_positive", "amount > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new TransferId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.SourceWalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("source_wallet_id")
            .IsRequired();

        builder.Property(x => x.TargetWalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("target_wallet_id")
            .IsRequired();

        builder.Property(x => x.SourceOperationId)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("source_operation_id")
            .IsRequired();

        builder.Property(x => x.TargetOperationId)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("target_operation_id")
            .IsRequired();

        builder.Property<decimal>("_amount")
            .HasColumnName("amount")
            .HasColumnType("numeric")
            .IsRequired();

        builder.Property(x => x.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property(x => x.TransferDate).HasColumnName("transfer_date").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.SourceWalletId).HasDatabaseName("ix_transfers_source_wallet");
        builder.HasIndex(x => x.TargetWalletId).HasDatabaseName("ix_transfers_target_wallet");
    }
}
