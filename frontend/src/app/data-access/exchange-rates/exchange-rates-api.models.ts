// Соответствует docs/api/openapi.yaml (схемы ExchangeRateQuote, LatestExchangeRates).
// ExchangeRateQuote хранит только id валют (docs/PROGRESS.md, W1.1) — код валюты для
// отображения подтягивается отдельно через ReferenceDataApiService (/currencies).

export interface ExchangeRateQuote {
  fromCurrencyId: string;
  toCurrencyId: string;
  /** Decimal как строка (ADR-0005) — не приводить к number. */
  rate: string;
}

export interface LatestExchangeRates {
  /** null, если ни одно обновление ещё не было успешным (UC-10). */
  lastSuccessfulUpdate: string | null;
  rates: ExchangeRateQuote[];
}
