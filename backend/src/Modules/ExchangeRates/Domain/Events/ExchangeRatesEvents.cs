using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Events for the RefreshExchangeRates command (ddd-model.md, §6, "ExchangeRates" section) —
/// describe the result of a whole refresh run (daily scheduled or manual, ADR-0001), not a
/// state change of one specific ExchangeRateQuote: a single run updates many pairs at once,
/// some of which may fail. Both events may be raised for the same run (partial success,
/// ADR-0001 p.6 — a currency unsupported by Frankfurter does not count: see
/// FrankfurterUnsupportedCurrencyException, such pairs are skipped silently, without raising
/// ExchangeRateUpdateFailed).
/// </summary>
public sealed record ExchangeRatePairResult(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, decimal Rate);

public sealed record ExchangeRatePairFailure(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, string Reason);

public sealed record ExchangeRatesUpdated(DateTimeOffset FetchedAt, IReadOnlyList<ExchangeRatePairResult> UpdatedPairs) : DomainEvent;

public sealed record ExchangeRateUpdateFailed(DateTimeOffset AttemptedAt, IReadOnlyList<ExchangeRatePairFailure> FailedPairs) : DomainEvent;
