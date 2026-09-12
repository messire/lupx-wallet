using LupexWallet.ExchangeRates.Application;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class ExchangeRatesUnitOfWork(ExchangeRatesDbContext dbContext) : IExchangeRatesUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
