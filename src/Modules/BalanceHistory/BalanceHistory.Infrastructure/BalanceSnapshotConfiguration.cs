using LupexWallet.BalanceHistory.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Маппинг BalanceSnapshot → balance_history.balance_snapshots (schema.md) — составной
/// первичный ключ (wallet_id, snapshot_date), без суррогатного id (см. комментарий в
/// BalanceHistory.Domain.BalanceSnapshot).
/// </summary>
public sealed class BalanceSnapshotConfiguration : IEntityTypeConfiguration<BalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<BalanceSnapshot> builder)
    {
        builder.ToTable("balance_snapshots");

        builder.HasKey(x => new { x.WalletId, x.SnapshotDate });

        builder.Property(x => x.WalletId)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("wallet_id")
            .IsRequired();

        builder.Property(x => x.SnapshotDate).HasColumnName("snapshot_date").IsRequired();

        builder.Property<decimal>("_balanceAmount").HasColumnName("balance_amount").HasColumnType("numeric").IsRequired();

        builder.Property(x => x.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.WalletId, x.SnapshotDate }).HasDatabaseName("ix_balance_snapshots_wallet_date_desc").IsDescending(false, true);
    }
}
