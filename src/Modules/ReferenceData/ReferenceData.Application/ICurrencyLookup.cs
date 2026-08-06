using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем ReferenceData для модуля ExchangeRates
/// (W1.1, направление "сверху вниз" — ExchangeRates уже ссылается на ReferenceData.Application,
/// как Operations уже делает это для IOperationTypeLookup, ADR-0007) — разрешение кода валюты
/// (например, "USD"), нужного для обращения к Frankfurter (ADR-0001 п.4: коды, а не Id).
/// </summary>
public interface ICurrencyLookup
{
    Task<CurrencyLookupResult?> GetAsync(CurrencyId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrencyLookupResult>> GetActiveAsync(CancellationToken cancellationToken);
}

public sealed record CurrencyLookupResult(CurrencyId Id, string Code, bool IsActive);
