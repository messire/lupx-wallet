using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>Maps FundAllocation to funds.fund_allocations (schema.md). WalletId is a plain id, no FK across schemas.</summary>
public sealed class FundAllocationConfiguration : IEntityTypeConfiguration<FundAllocation>
{
    public void Configure(EntityTypeBuilder<FundAllocation> builder)
    {
        builder.ToTable("fund_allocations", table =>
            table.HasCheckConstraint("ck_fund_allocations_amount_non_negative", "amount >= 0"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(a => a.FundId)
            .HasConversion(id => id.Value, value => new FundId(value))
            .HasColumnName("fund_id")
            .IsRequired();

        builder.Property(a => a.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("wallet_id")
            .IsRequired();

        builder.Property(a => a.Amount).HasColumnName("amount").HasColumnType("numeric").IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(a => new { a.FundId, a.WalletId }).IsUnique().HasDatabaseName("ux_fund_allocations_fund_wallet");
        builder.HasIndex(a => a.WalletId).HasDatabaseName("ix_fund_allocations_wallet");
    }
}
