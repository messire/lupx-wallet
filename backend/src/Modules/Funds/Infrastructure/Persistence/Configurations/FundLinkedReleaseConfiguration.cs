using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>
/// Maps FundLinkedRelease to funds.fund_linked_releases (schema.md). OperationId and WalletId are
/// plain ids, no FK across schemas.
/// </summary>
public sealed class FundLinkedReleaseConfiguration : IEntityTypeConfiguration<FundLinkedRelease>
{
    public void Configure(EntityTypeBuilder<FundLinkedRelease> builder)
    {
        builder.ToTable("fund_linked_releases", table =>
            table.HasCheckConstraint("ck_fund_linked_releases_amount_non_negative", "amount >= 0"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(r => r.FundId)
            .HasConversion(id => id.Value, value => new FundId(value))
            .HasColumnName("fund_id")
            .IsRequired();

        builder.Property(r => r.OperationId)
            .HasConversion(id => id.Value, value => new OperationId(value))
            .HasColumnName("operation_id")
            .IsRequired();

        builder.Property(r => r.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("wallet_id")
            .IsRequired();

        builder.Property(r => r.Amount).HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(r => new { r.FundId, r.OperationId, r.WalletId })
            .IsUnique()
            .HasDatabaseName("ux_fund_linked_releases_fund_operation_wallet");
        builder.HasIndex(r => r.OperationId).HasDatabaseName("ix_fund_linked_releases_operation");
    }
}
