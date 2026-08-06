using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

public interface ILatestExchangeRateRepository
{
    void Add(ExchangeRateQuote quote);

    void Remove(ExchangeRateQuote quote);

    Task<ExchangeRateQuote?> FindAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExchangeRateQuote>> GetAllAsync(CancellationToken cancellationToken);
}
