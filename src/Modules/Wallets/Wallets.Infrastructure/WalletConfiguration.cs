using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>Маппинг Wallet → wallets.wallets (schema.md).</summary>
public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets", table =>
        {
            table.HasCheckConstraint("ck_wallets_primary_not_archived", "NOT (is_primary AND is_archived)");
            table.HasCheckConstraint("ck_wallets_primary_included_in_total", "NOT is_primary OR include_in_total");
        });

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WalletId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(200).IsRequired();

        builder.Property(w => w.WalletTypeId)
            .HasConversion(id => id.Value, value => new WalletTypeId(value))
            .HasColumnName("wallet_type_id")
            .IsRequired();

        builder.Property(w => w.PurposeDescription).HasColumnName("purpose_description");

        builder.Property(w => w.CurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("currency_id")
            .IsRequired();

        builder.Property<decimal>("_initialBalanceAmount")
            .HasColumnName("initial_balance_amount")
            .HasColumnType("numeric")
            .IsRequired();

        builder.Property<decimal>("_currentBalanceAmount")
            .HasColumnName("current_balance_amount")
            .HasColumnType("numeric")
            .IsRequired();

        builder.Property(w => w.AccountingStartDate).HasColumnName("accounting_start_date").IsRequired();
        builder.Property(w => w.IncludeInTotal).HasColumnName("include_in_total").IsRequired();
        builder.Property(w => w.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(w => w.IsArchived).HasColumnName("is_archived").IsRequired();
        builder.Property(w => w.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(w => w.Color).HasColumnName("color").HasMaxLength(32);
        builder.Property(w => w.Icon).HasColumnName("icon").HasMaxLength(64);

        builder.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // ux_wallets_single_primary (schema.md): частичный уникальный индекс — не более
        // одного кошелька с is_primary = true.
        builder.HasIndex(w => w.IsPrimary)
            .HasDatabaseName("ux_wallets_single_primary")
            .IsUnique()
            .HasFilter("is_primary");

        builder.HasIndex(w => w.DisplayOrder)
            .HasDatabaseName("ix_wallets_display_order")
            .HasFilter("NOT is_archived");
        builder.HasIndex(w => w.WalletTypeId).HasDatabaseName("ix_wallets_wallet_type");
        builder.HasIndex(w => w.CurrencyId).HasDatabaseName("ix_wallets_currency");
    }
}
