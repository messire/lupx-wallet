using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;

namespace LupexWallet.Reporting.Application;

/// <summary>A latest known rate applied to a summary or forecast (ADR-0010 §7: rate date and source are shown).</summary>
public sealed record AppliedRateDto(Guid FromCurrencyId, Guid ToCurrencyId, decimal Rate, DateOnly RateDate, string Source);

/// <summary>Amounts in a currency without a known rate are left out of the converted totals; the reason is returned.</summary>
public sealed record MissingRateDto(Guid CurrencyId, string Reason, int ItemCount);

/// <summary>
/// Converts amounts to the primary-wallet currency by the latest known rate (one lookup per
/// currency per request). Missing rates are reported, never replaced by 1:1 (ADR-0004 §9).
/// </summary>
public sealed class LatestRateConverter(IExchangeRateLookup exchangeRateLookup, ICurrencyLookup currencyLookup, CurrencyId targetCurrencyId)
{
    /// <summary>Rate source of every rate (ADR-0004: the only rate provider).</summary>
    public const string Source = "Frankfurter";

    private readonly Dictionary<CurrencyId, (decimal Rate, DateOnly Date)?> _rates = [];
    private readonly Dictionary<CurrencyId, int> _missing = [];

    public CurrencyId TargetCurrencyId => targetCurrencyId;

    /// <summary>Converted amount, or null when the currency has no known rate (counted in <see cref="MissingRatesAsync"/>).</summary>
    public async Task<decimal?> ConvertAsync(decimal amount, CurrencyId currencyId, CancellationToken cancellationToken)
    {
        var rate = await RateAsync(currencyId, cancellationToken);
        if (rate is null)
        {
            _missing[currencyId] = _missing.GetValueOrDefault(currencyId) + 1;
            return null;
        }

        return amount * rate.Value;
    }

    /// <summary>Rate to the target currency (1 for the target itself), or null; not counted as a missing item.</summary>
    public async Task<decimal?> RateAsync(CurrencyId currencyId, CancellationToken cancellationToken)
    {
        if (currencyId == targetCurrencyId)
        {
            return 1m;
        }

        if (!_rates.TryGetValue(currencyId, out var rate))
        {
            rate = await exchangeRateLookup.GetLatestRateWithDateAsync(currencyId, targetCurrencyId, cancellationToken);
            _rates[currencyId] = rate;
        }

        return rate?.Rate;
    }

    /// <summary>Rates of other currencies actually applied, by currency.</summary>
    public IReadOnlyList<AppliedRateDto> AppliedRates =>
        _rates
            .Where(r => r.Value is not null)
            .Select(r => new AppliedRateDto(r.Key.Value, targetCurrencyId.Value, r.Value!.Value.Rate, r.Value.Value.Date, Source))
            .OrderBy(r => r.FromCurrencyId)
            .ToList();

    /// <summary>The earliest applied rate date (the most conservative "how old are the rates"); today if none applied.</summary>
    public DateOnly RatesAsOfDate(DateOnly today) =>
        _rates.Values.Where(r => r is not null).Select(r => r!.Value.Date).DefaultIfEmpty(today).Min();

    public async Task<IReadOnlyList<MissingRateDto>> MissingRatesAsync(CancellationToken cancellationToken)
    {
        var result = new List<MissingRateDto>();
        foreach (var (currencyId, count) in _missing)
        {
            result.Add(new MissingRateDto(currencyId.Value, await NoRateReasonAsync(currencyId, cancellationToken), count));
        }

        return result;
    }

    public async Task<string> NoRateReasonAsync(CurrencyId currencyId, CancellationToken cancellationToken)
    {
        var code = (await currencyLookup.GetAsync(currencyId, cancellationToken))?.Code ?? currencyId.Value.ToString();
        return $"Курс валюты {code} недоступен.";
    }
}
