using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// ADR-0009, case c — one of the ReferenceData.Application.IReferenceItemUsageProbe
/// implementations: data owner is ExchangeRates. Checks Currency usage in
/// exchange_rates.latest_exchange_rates/historical_exchange_rates (a targeted EXISTS, not a
/// collection dump). WalletType/OperationType are not relevant to this module — false.
/// Implemented in W2.5 (see docs/PROGRESS.md, W1.1 "known simplification" — deliberately
/// deferred until IReferenceItemUsageProbe appeared in W2.5). Registered in
/// AddExchangeRatesModule.
/// </summary>
public sealed class ExchangeRatesReferenceItemUsageProbe(ExchangeRatesDbContext dbContext) : IReferenceItemUsageProbe
{
    public async Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken)
    {
        if (kind != ReferenceItemKind.Currency)
        {
            return false;
        }

        var currencyId = new CurrencyId(referenceItemId);

        var usedInLatest = await dbContext.LatestExchangeRates.AnyAsync(
            q => q.FromCurrencyId == currencyId || q.ToCurrencyId == currencyId, cancellationToken);
        if (usedInLatest)
        {
            return true;
        }

        return await dbContext.HistoricalExchangeRates.AnyAsync(
            q => q.FromCurrencyId == currencyId || q.ToCurrencyId == currencyId, cancellationToken);
    }
}
