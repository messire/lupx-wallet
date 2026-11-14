using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>Maps AvailableCurrency to reference_data.available_currencies (ADR-0004 §7).</summary>
public sealed class AvailableCurrencyConfiguration : IEntityTypeConfiguration<AvailableCurrency>
{
    public void Configure(EntityTypeBuilder<AvailableCurrency> builder)
    {
        builder.ToTable("available_currencies");

        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(16).ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RefreshedAt).HasColumnName("refreshed_at").IsRequired();
    }
}
