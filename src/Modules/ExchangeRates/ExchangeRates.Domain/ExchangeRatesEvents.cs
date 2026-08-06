using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// События команды RefreshExchangeRates (ddd-model.md, §6, раздел "ExchangeRates") —
/// описывают результат прогона обновления целиком (плановый раз в сутки или ручной,
/// ADR-0001), а не изменение состояния одного конкретного ExchangeRateQuote: за один
/// прогон обновляется сразу множество пар, часть из которых может завершиться ошибкой.
/// Оба события могут быть подняты за один и тот же прогон (частичный успех, п.6 ADR-0001 —
/// валюта, не поддерживаемая Frankfurter, не в счёт: см. FrankfurterUnsupportedCurrencyException,
/// такие пары пропускаются молча, не порождая ExchangeRateUpdateFailed).
/// </summary>
public sealed record ExchangeRatePairResult(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, decimal Rate);

public sealed record ExchangeRatePairFailure(CurrencyId FromCurrencyId, CurrencyId ToCurrencyId, string Reason);

public sealed record ExchangeRatesUpdated(DateTimeOffset FetchedAt, IReadOnlyList<ExchangeRatePairResult> UpdatedPairs) : DomainEvent;

public sealed record ExchangeRateUpdateFailed(DateTimeOffset AttemptedAt, IReadOnlyList<ExchangeRatePairFailure> FailedPairs) : DomainEvent;
