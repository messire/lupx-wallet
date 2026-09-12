using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class LatestExchangeRateRepository(ExchangeRatesDbContext dbContext) : ILatestExchangeRateRepository
{
    public void Add(ExchangeRateQuote quote) => dbContext.LatestExchangeRates.Add(quote);

    public void Remove(ExchangeRateQuote quote) => dbContext.LatestExchangeRates.Remove(quote);

    public Task<ExchangeRateQuote?> FindAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken) =>
        dbContext.LatestExchangeRates.FirstOrDefaultAsync(
            q => q.FromCurrencyId == from && q.ToCurrencyId == to, cancellationToken);

    public async Task<IReadOnlyList<ExchangeRateQuote>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.LatestExchangeRates.AsNoTracking().ToListAsync(cancellationToken);
}
