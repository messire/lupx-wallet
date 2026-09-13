using LupexWallet.ExchangeRates.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>Schema "exchange_rates" (schema.md) — a separate DbContext per module (ADR-0006).</summary>
public sealed class ExchangeRatesDbContext(DbContextOptions<ExchangeRatesDbContext> options) : DbContext(options)
{
    public DbSet<ExchangeRateQuote> LatestExchangeRates => Set<ExchangeRateQuote>();
    public DbSet<HistoricalExchangeRate> HistoricalExchangeRates => Set<HistoricalExchangeRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("exchange_rates");
        modelBuilder.ApplyConfiguration(new ExchangeRateQuoteConfiguration());
        modelBuilder.ApplyConfiguration(new HistoricalExchangeRateConfiguration());
    }
}
