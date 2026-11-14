namespace LupexWallet.ExchangeRates.Application;

public interface IExchangeRatesUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Detaches every tracked entity — keeps a long history load from accumulating thousands of tracked rows.</summary>
    void ClearChangeTracker();
}
