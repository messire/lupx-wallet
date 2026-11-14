using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>Maps HistoricalRateCoverage to exchange_rates.historical_rate_coverage (ADR-0004, §9).</summary>
public sealed class HistoricalRateCoverageConfiguration : IEntityTypeConfiguration<HistoricalRateCoverage>
{
    public void Configure(EntityTypeBuilder<HistoricalRateCoverage> builder)
    {
        builder.ToTable("historical_rate_coverage", table =>
        {
            table.HasCheckConstraint("ck_historical_coverage_distinct_currencies", "from_currency_id <> to_currency_id");
            table.HasCheckConstraint("ck_historical_coverage_range", "covered_from <= covered_to");
        });

        builder.HasKey(x => new { x.FromCurrencyId, x.ToCurrencyId });

        builder.Property(x => x.FromCurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("from_currency_id")
            .IsRequired();

        builder.Property(x => x.ToCurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("to_currency_id")
            .IsRequired();

        builder.Property(x => x.CoveredFrom).HasColumnName("covered_from").IsRequired();
        builder.Property(x => x.CoveredTo).HasColumnName("covered_to").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
