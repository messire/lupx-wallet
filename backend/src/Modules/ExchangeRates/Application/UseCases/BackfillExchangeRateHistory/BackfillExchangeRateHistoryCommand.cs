using LupexWallet.ExchangeRates.Domain;
using MediatR;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// One pass of the rate history load (ADR-0004, §9): for every active currency except the primary
/// one, loads rates to the primary currency for [earliest accounting start date − 7 days; today]
/// in chunks of at most <c>ExchangeRates:HistoryChunkDays</c> days, upserts them into
/// historical_exchange_rates and records progress in historical_rate_coverage. A repeated pass
/// requests only what the coverage lacks (the left edge if the earliest date moved, the right edge
/// from covered_to − 7 days to today), so an interrupted load resumes where it stopped. Does nothing
/// without a primary wallet.
/// <para>
/// Like <see cref="RefreshExchangeRatesCommand"/> deliberately not an ICommand: it makes HTTP calls,
/// which must stay outside a TransactionScope (ADR-0001 §4); every chunk is saved on its own.
/// The handler (BackfillExchangeRateHistoryCommandHandler) lives in ExchangeRates.Infrastructure and
/// is registered manually, for the same reason as the refresh handler.
/// </para>
/// </summary>
public sealed record BackfillExchangeRateHistoryCommand : IRequest<BackfillResult>;

/// <summary>Outcome of a pass: ranges loaded per pair and pairs whose load failed (the source was unavailable).</summary>
public sealed record BackfillResult(
    IReadOnlyList<HistoryRangeLoaded> Loaded,
    IReadOnlyList<ExchangeRatePairFailure> Failed)
{
    public static BackfillResult Empty { get; } = new([], []);

    public bool HadFailures => Failed.Count > 0;
}
