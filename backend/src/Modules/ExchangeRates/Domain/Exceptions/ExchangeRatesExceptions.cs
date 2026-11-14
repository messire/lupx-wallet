using System.Globalization;

namespace LupexWallet.ExchangeRates.Domain;

public abstract class ExchangeRatesDomainException(string message) : Exception(message);

public sealed class InvalidExchangeRateException(decimal rate)
    : ExchangeRatesDomainException($"Курс должен быть больше нуля (получено {rate.ToString(CultureInfo.InvariantCulture)}).");

public sealed class SameCurrencyExchangeRateException(Guid currencyId)
    : ExchangeRatesDomainException($"Курс между одинаковыми валютами не имеет смысла ({currencyId}).");

public sealed class InvalidCoverageRangeException(DateOnly coveredFrom, DateOnly coveredTo)
    : ExchangeRatesDomainException(
        $"Начало покрытия истории курсов ({coveredFrom:yyyy-MM-dd}) позже его конца ({coveredTo:yyyy-MM-dd}).");
