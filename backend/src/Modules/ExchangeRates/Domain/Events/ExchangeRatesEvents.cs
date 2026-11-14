using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Events for the RefreshExchangeRates command (ddd-model.md, §6, "ExchangeRates" section) —
/// describe the result of a whole refresh run (daily scheduled or manual, ADR-0004), not a
/// state change of one specific ExchangeRateQuote: a single run updates many pairs at once,
/// some of which may fail. Both events may be raised for the same run (partial success,
/// ADR-0004 p.6 — a currency unsupported by Frankfurter does not count: see
/// FrankfurterUnsupportedCurrencyException, such pairs are skipped silently, without raising
/// ExchangeRateUpdateFailed).
/// </summary>
public sealed record ExchangeRatePairResult(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, decimal Rate);

public sealed record ExchangeRatePairFailure(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, string Reason);

public sealed record ExchangeRatesUpdated(DateTimeOffset FetchedAt, IReadOnlyList<ExchangeRatePairResult> UpdatedPairs) : DomainEvent;

public sealed record ExchangeRateUpdateFailed(DateTimeOffset AttemptedAt, IReadOnlyList<ExchangeRatePairFailure> FailedPairs) : DomainEvent;

/// <summary>Date range of historical rates loaded for one pair during a history load pass (ADR-0004, §9).</summary>
public sealed record HistoryRangeLoaded(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, DateOnly RangeFrom, DateOnly RangeTo);

/// <summary>
/// Result of one history load pass (ADR-0004, §9): ranges loaded and pairs that failed. Raised
/// once per pass, only if something was loaded or failed — like ExchangeRatesUpdated, it
/// describes a whole run, not one aggregate.
/// </summary>
public sealed record ExchangeRateHistoryLoaded(
    DateTimeOffset At,
    IReadOnlyList<HistoryRangeLoaded> Loaded,
    IReadOnlyList<ExchangeRatePairFailure> Failed) : DomainEvent;
