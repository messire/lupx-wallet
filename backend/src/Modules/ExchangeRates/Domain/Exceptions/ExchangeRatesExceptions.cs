using System.Globalization;

namespace LupexWallet.ExchangeRates.Domain;

public abstract class ExchangeRatesDomainException(string message) : Exception(message);

public sealed class InvalidExchangeRateException(decimal rate)
    : ExchangeRatesDomainException($"Курс должен быть больше нуля (получено {rate.ToString(CultureInfo.InvariantCulture)}).");

public sealed class SameCurrencyExchangeRateException(Guid currencyId)
    : ExchangeRatesDomainException($"Курс между одинаковыми валютами не имеет смысла ({currencyId}).");
