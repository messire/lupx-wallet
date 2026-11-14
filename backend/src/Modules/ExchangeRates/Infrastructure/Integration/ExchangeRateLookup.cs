using LupexWallet.ExchangeRates.Application;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Implementation of the public IExchangeRateLookup contract (consumer — Reporting). Reads the
/// database only: it neither calls the rate service nor writes anything (ADR-0004, §8). History is
/// filled by the background load (BackfillExchangeRateHistoryCommand, §9).
/// </summary>
public sealed class ExchangeRateLookup(
    ILatestExchangeRateRepository latestRepository,
    IHistoricalExchangeRateRepository historicalRepository) : IExchangeRateLookup
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

    public async Task<(decimal Rate, DateOnly RatesAsOfDate)?> GetHistoricalRateAsync(
        CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken)
    {
        if (from == to)
        {
            return (1m, date);
        }

        var closest = await historicalRepository.FindClosestBeforeAsync(from, to, date, cancellationToken);
        return closest is null ? null : (closest.Rate.Rate, closest.RateDate);
    }
}
