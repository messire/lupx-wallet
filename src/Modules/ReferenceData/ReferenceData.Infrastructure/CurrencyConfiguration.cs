using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>Маппинг Currency → reference_data.currencies (schema.md).</summary>
public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Код уникален глобально (включая неактивные) — schema.md.
        builder.HasIndex(x => x.Code).HasDatabaseName("ux_currencies_code").IsUnique();
    }
}
