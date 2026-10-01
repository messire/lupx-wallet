using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class LatestExchangeRateRepository(ExchangeRatesDbContext dbContext) : ILatestExchangeRateRepository
{
    public void Add(ExchangeRateQuote quote) => dbContext.LatestExchangeRates.Add(quote);

    /// <summary>Removes the tracked instance of the same pair if there is one (a no-tracking read would otherwise clash on the key).</summary>
    public void Remove(ExchangeRateQuote quote)
    {
        var tracked = dbContext.LatestExchangeRates.Local.FirstOrDefault(
            q => q.FromCurrencyId == quote.FromCurrencyId && q.ToCurrencyId == quote.ToCurrencyId);
        dbContext.LatestExchangeRates.Remove(tracked ?? quote);
    }

    public Task<ExchangeRateQuote?> FindAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken) =>
        dbContext.LatestExchangeRates.FirstOrDefaultAsync(
            q => q.FromCurrencyId == from && q.ToCurrencyId == to, cancellationToken);

    public async Task<IReadOnlyList<ExchangeRateQuote>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.LatestExchangeRates.AsNoTracking().ToListAsync(cancellationToken);

    public Task RemoveAllForCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken) =>
        dbContext.LatestExchangeRates
            .Where(q => q.FromCurrencyId == currencyId || q.ToCurrencyId == currencyId)
            .ExecuteDeleteAsync(cancellationToken);
}
