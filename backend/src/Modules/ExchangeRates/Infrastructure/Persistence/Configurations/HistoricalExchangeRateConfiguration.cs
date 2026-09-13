using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>Maps HistoricalExchangeRate to exchange_rates.historical_exchange_rates (schema.md).</summary>
public sealed class HistoricalExchangeRateConfiguration : IEntityTypeConfiguration<HistoricalExchangeRate>
{
    public void Configure(EntityTypeBuilder<HistoricalExchangeRate> builder)
    {
        builder.ToTable("historical_exchange_rates", table =>
        {
            table.HasCheckConstraint("ck_historical_rates_distinct_currencies", "from_currency_id <> to_currency_id");
            table.HasCheckConstraint("ck_historical_rates_positive", "rate > 0");
        });

        builder.HasKey(x => new { x.FromCurrencyId, x.ToCurrencyId, x.RateDate });

        builder.Property(x => x.FromCurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("from_currency_id")
            .IsRequired();

        builder.Property(x => x.ToCurrencyId)
            .HasConversion(id => id.Value, value => new CurrencyId(value))
            .HasColumnName("to_currency_id")
            .IsRequired();

        builder.Property(x => x.RateDate).HasColumnName("rate_date").IsRequired();

        builder.Property<decimal>("_rate").HasColumnName("rate").HasColumnType("numeric").IsRequired();

        builder.Property(x => x.FetchedAt).HasColumnName("fetched_at").IsRequired();
    }
}
