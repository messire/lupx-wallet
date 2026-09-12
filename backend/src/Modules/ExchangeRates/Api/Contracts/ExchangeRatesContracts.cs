namespace LupexWallet.ExchangeRates.Api;

// DTO по контракту docs/api/openapi.yaml (схемы ExchangeRateQuote, LatestExchangeRates).

public sealed record ExchangeRateQuoteResponse(Guid FromCurrencyId, Guid ToCurrencyId, string Rate);

public sealed record LatestExchangeRatesResponse(DateTimeOffset? LastSuccessfulUpdate, IReadOnlyList<ExchangeRateQuoteResponse> Rates);
