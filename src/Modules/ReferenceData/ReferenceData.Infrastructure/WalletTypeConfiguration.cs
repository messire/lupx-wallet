using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>Маппинг WalletType → reference_data.wallet_types (schema.md).</summary>
public sealed class WalletTypeConfiguration : IEntityTypeConfiguration<WalletType>
{
    public void Configure(EntityTypeBuilder<WalletType> builder)
    {
        builder.ToTable("wallet_types");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new WalletTypeId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Уникальность имени только среди активных (schema.md).
        builder.HasIndex(x => x.Name)
            .HasDatabaseName("ux_wallet_types_active_name")
            .IsUnique()
            .HasFilter("is_active");
    }
}
