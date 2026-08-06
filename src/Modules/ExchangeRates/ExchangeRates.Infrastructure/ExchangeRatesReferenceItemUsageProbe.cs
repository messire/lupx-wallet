using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// ADR-0009, случай c — одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe:
/// владелец данных ExchangeRates. Проверяет Currency по факту использования в
/// exchange_rates.latest_exchange_rates/historical_exchange_rates (точечный EXISTS, не
/// выгрузка коллекций). WalletType/OperationType этому модулю не релевантны — false.
/// Реализовано в W2.5 (см. docs/PROGRESS.md, W1.1 «Известное упрощение» — было сознательно
/// отложено до появления IReferenceItemUsageProbe в W2.5). Зарегистрирована в
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
