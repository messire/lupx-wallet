using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Implements <see cref="IAvailableCurrencyCatalog"/>: full replacement of
/// <c>reference_data.available_currencies</c> plus renaming of active currencies to the service
/// names (ADR-0004 §7), in one SaveChanges.
/// </summary>
public sealed class AvailableCurrencyCatalog(ReferenceDataDbContext dbContext) : IAvailableCurrencyCatalog
{
    public async Task ReplaceAsync(IReadOnlyList<AvailableCurrencyItem> items, CancellationToken cancellationToken)
    {
        // Last occurrence wins for duplicated codes; rows without a code or name are ignored.
        var incoming = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            incoming[item.Code.Trim().ToUpperInvariant()] = item.Name.Trim();
        }

        // An empty list is a failed load, not "nothing is available": keep the stored list.
        if (incoming.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await dbContext.AvailableCurrencies.ToDictionaryAsync(x => x.Code, cancellationToken);

        foreach (var (code, name) in incoming)
        {
            if (existing.TryGetValue(code, out var row))
            {
                row.Update(name, now);
            }
            else
            {
                dbContext.AvailableCurrencies.Add(AvailableCurrency.Create(code, name, now));
            }
        }

        dbContext.AvailableCurrencies.RemoveRange(existing.Values.Where(x => !incoming.ContainsKey(x.Code)));

        var active = await dbContext.Currencies.ToListAsync(cancellationToken);
        foreach (var currency in active)
        {
            if (incoming.TryGetValue(currency.Code, out var serviceName))
            {
                currency.Rename(serviceName);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
