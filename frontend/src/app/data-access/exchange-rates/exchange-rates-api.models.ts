// Mirrors docs/api/openapi.yaml (ExchangeRateQuote, LatestExchangeRates schemas).
// ExchangeRateQuote only stores currency ids — the display currency code is
// fetched separately via ReferenceDataApiService (/currencies).

export interface ExchangeRateQuote {
  fromCurrencyId: string;
  toCurrencyId: string;
  /** Decimal as a string (ADR-0005) — do not cast to number. */
  rate: string;
}

export interface LatestExchangeRates {
  /** null if no update has succeeded yet (UC-10). */
  lastSuccessfulUpdate: string | null;
  rates: ExchangeRateQuote[];
}
