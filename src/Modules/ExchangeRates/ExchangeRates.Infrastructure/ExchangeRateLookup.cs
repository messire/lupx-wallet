using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Реализация публичного контракта IExchangeRateLookup (для будущего потребителя —
/// Reporting, W2.1). GetOrFetchHistoricalRateAsync пишет в БД (append-only кэш
/// historical_exchange_rates) вне общей транзакции команды-потребителя — см. риск-заметку
/// docs/PROGRESS.md, W2.1.
/// </summary>
public sealed class ExchangeRateLookup(
    ILatestExchangeRateRepository latestRepository,
    IHistoricalExchangeRateRepository historicalRepository,
    IExchangeRatesUnitOfWork unitOfWork,
    ICurrencyLookup currencyLookup,
    IFrankfurterClient frankfurterClient,
    ILogger<ExchangeRateLookup> logger) : IExchangeRateLookup
{
    public async Task<decimal?> GetRateAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken)
    {
        if (from == to)
        {
            return 1m;
        }

        var quote = await latestRepository.FindAsync(from, to, cancellationToken);
        return quote?.Rate.Rate;
    }

    public async Task<(decimal Rate, DateOnly RatesAsOfDate)?> GetOrFetchHistoricalRateAsync(
        CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken)
    {
        if (from == to)
        {
            return (1m, date);
        }

        var cached = await historicalRepository.FindAsync(from, to, date, cancellationToken);
        if (cached is not null)
        {
            return (cached.Rate.Rate, cached.RateDate);
        }

        var closest = await historicalRepository.FindClosestBeforeAsync(from, to, date, cancellationToken);

        var fromCurrency = await currencyLookup.GetAsync(from, cancellationToken);
        var toCurrency = await currencyLookup.GetAsync(to, cancellationToken);
        if (fromCurrency is null || toCurrency is null)
        {
            return closest is null ? null : (closest.Rate.Rate, closest.RateDate);
        }

        try
        {
            var (rate, ratesAsOfDate) = await frankfurterClient.GetHistoricalRateAsync(fromCurrency.Code, toCurrency.Code, date, cancellationToken);

            var alreadyCachedForActualDate = ratesAsOfDate != date
                && await historicalRepository.FindAsync(from, to, ratesAsOfDate, cancellationToken) is not null;

            if (!alreadyCachedForActualDate)
            {
                historicalRepository.Add(HistoricalExchangeRate.Create(from, to, ratesAsOfDate, new ExchangeRateValue(rate), DateTimeOffset.UtcNow));
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return (rate, ratesAsOfDate);
        }
        catch (FrankfurterClientException ex)
        {
            // Источник недоступен/пара не поддерживается — используем ближайший ранее
            // закэшированный курс (UC-21 fallback), если он есть.
            logger.LogWarning(ex, "Не удалось получить исторический курс {From}->{To} на {Date}", from, to, date);
            return closest is null ? null : (closest.Rate.Rate, closest.RateDate);
        }
    }
}
