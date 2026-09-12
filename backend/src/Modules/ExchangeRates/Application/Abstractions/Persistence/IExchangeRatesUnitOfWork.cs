namespace LupexWallet.ExchangeRates.Application;

public interface IExchangeRatesUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
