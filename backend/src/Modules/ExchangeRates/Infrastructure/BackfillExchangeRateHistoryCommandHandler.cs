using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>Options of the history load: <c>ExchangeRates:HistoryChunkDays</c> (ADR-0004, §9).</summary>
public sealed record ExchangeRateHistoryOptions(int ChunkDays = ExchangeRateHistoryOptions.DefaultChunkDays)
{
    public const int DefaultChunkDays = 366;
}

/// <summary>
/// Handler for BackfillExchangeRateHistoryCommand (ADR-0004, §9); lives in Infrastructure and is
/// registered manually, for the same reasons as RefreshExchangeRatesCommandHandler.
/// <para>
/// Per pair (active currency → primary currency) the stored coverage [covered_from; covered_to]
/// tells what is missing: no row — the whole period [requiredFrom; today]; otherwise the right edge
/// [covered_to − 7; today] (the overlap re-reads days whose rate may have been refined) and the left
/// edge [requiredFrom; covered_from − 1]. Chunks are at most <c>ChunkDays</c> long and each is one
/// request, one upsert and one SaveChanges together with the coverage shift, so the coverage is always
/// contiguous and an aborted pass resumes where it stopped. The left edge is loaded from new dates to
/// old; the right edge from old to new — a long gap must not leave a hole between the covered range and
/// the newest chunk. requiredFrom reaches 7 days before the earliest accounting start date, so the
/// closest earlier rate exists for a weekend on the left border.
/// </para>
/// </summary>
public sealed class BackfillExchangeRateHistoryCommandHandler(
    IHistoricalExchangeRateRepository historyRepository,
    IHistoricalRateCoverageRepository coverageRepository,
    IExchangeRatesUnitOfWork unitOfWork,
    IFrankfurterClient frankfurterClient,
    IWalletCurrencySet walletCurrencySet,
    ICurrencyLookup currencyLookup,
    IDomainEventDispatcher eventDispatcher,
    ExchangeRateHistoryOptions options,
    ILogger<BackfillExchangeRateHistoryCommandHandler> logger)
    : IRequestHandler<BackfillExchangeRateHistoryCommand, BackfillResult>
{
    private const int LeftMarginDays = 7;
    private const int RightOverlapDays = 7;

    public async Task<BackfillResult> Handle(BackfillExchangeRateHistoryCommand request, CancellationToken cancellationToken)
    {
        var currencySet = await walletCurrencySet.GetAsync(cancellationToken);
        if (currencySet.PrimaryCurrencyId is not { } primaryCurrencyId)
        {
            return BackfillResult.Empty;
        }

        var activeCurrencies = await currencyLookup.GetAllAsync(cancellationToken);
        var primaryCurrency = activeCurrencies.FirstOrDefault(c => c.Id == primaryCurrencyId);
        if (primaryCurrency is null)
        {
            logger.LogWarning("Основная валюта {Currency} не найдена среди активных — дозагрузка истории пропущена", primaryCurrencyId);
            return BackfillResult.Empty;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var requiredFrom = Earlier(currencySet.EarliestAccountingStartDate ?? today, today).AddDays(-LeftMarginDays);
        var chunkDays = Math.Max(1, options.ChunkDays);

        var loaded = new List<HistoryRangeLoaded>();
        var failed = new List<ExchangeRatePairFailure>();

        foreach (var currency in activeCurrencies.Where(c => c.Id != primaryCurrencyId))
        {
            var pair = new Pair(currency.Id, currency.Code, primaryCurrencyId, primaryCurrency.Code);
            try
            {
                await BackfillPairAsync(pair, requiredFrom, today, chunkDays, loaded, cancellationToken);
            }
            catch (FrankfurterUnavailableException ex)
            {
                // The source is down: stop the pass instead of spending pairs x chunks x retries.
                // Chunks already saved stay, the next pass continues from the coverage.
                logger.LogWarning(ex, "Frankfurter недоступен для пары {From}->{To} — проход дозагрузки прерван", pair.FromCode, pair.ToCode);
                failed.Add(new ExchangeRatePairFailure(pair.FromId, pair.ToId, ex.Message));
                break;
            }
            catch (FrankfurterUnsupportedCurrencyException ex)
            {
                logger.LogInformation(ex, "Валюта не поддерживается Frankfurter — история пары {From}->{To} пропущена", pair.FromCode, pair.ToCode);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // The currency was removed while the pass was running (its coverage row is gone).
                logger.LogWarning(ex, "Пара {From}->{To} удалена во время дозагрузки и пропущена", pair.FromCode, pair.ToCode);
            }
            finally
            {
                unitOfWork.ClearChangeTracker();
            }
        }

        // History of pairs to a former primary currency and of currencies that are no longer active
        // is dead weight (ADR-0004 §9, change of the primary wallet).
        await historyRepository.RemoveStaleAsync(
            primaryCurrencyId, activeCurrencies.Select(c => c.Id).ToHashSet(), cancellationToken);
        await coverageRepository.RemoveStaleAsync(
            primaryCurrencyId, activeCurrencies.Select(c => c.Id).ToHashSet(), cancellationToken);

        if (loaded.Count > 0 || failed.Count > 0)
        {
            await eventDispatcher.DispatchAsync([new ExchangeRateHistoryLoaded(DateTimeOffset.UtcNow, loaded, failed)], cancellationToken);
        }

        return new BackfillResult(loaded, failed);
    }

    private async Task BackfillPairAsync(
        Pair pair, DateOnly requiredFrom, DateOnly today, int chunkDays, List<HistoryRangeLoaded> loaded, CancellationToken cancellationToken)
    {
        var coverage = await coverageRepository.FindAsync(pair.FromId, pair.ToId, cancellationToken);

        if (coverage is not null && coverage.CoveredTo < today)
        {
            await LoadRightEdgeAsync(pair, coverage, today, chunkDays, loaded, cancellationToken);
        }

        var leftEnd = coverage is null ? today : coverage.CoveredFrom.AddDays(-1);
        if (requiredFrom <= leftEnd)
        {
            await LoadLeftEdgeAsync(pair, coverage, requiredFrom, leftEnd, chunkDays, loaded, cancellationToken);
        }
    }

    /// <summary>Extends the coverage to <paramref name="today"/>, chunks from old to new.</summary>
    private async Task LoadRightEdgeAsync(
        Pair pair, HistoricalRateCoverage coverage, DateOnly today, int chunkDays, List<HistoryRangeLoaded> loaded, CancellationToken cancellationToken)
    {
        var chunkFrom = Later(coverage.CoveredFrom, coverage.CoveredTo.AddDays(-RightOverlapDays));
        DateOnly? loadedFrom = null;
        DateOnly? loadedTo = null;

        try
        {
            while (chunkFrom <= today)
            {
                var chunkTo = Earlier(today, chunkFrom.AddDays(chunkDays - 1));
                var rates = await frankfurterClient.GetRatesForPeriodAsync(pair.FromCode, pair.ToCode, chunkFrom, chunkTo, cancellationToken);

                // An empty chunk adds nothing and the coverage does not move past it: a delisted currency
                // costs one request per pass. If a later chunk has data, the coverage jumps over the empty
                // one — the service has no rates there.
                if (rates.Count > 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    await UpsertAsync(pair, rates, chunkFrom, chunkTo, now, cancellationToken);
                    coverage.ExtendRight(chunkTo, now);
                    await unitOfWork.SaveChangesAsync(cancellationToken);

                    loadedFrom ??= chunkFrom;
                    loadedTo = chunkTo;
                }

                chunkFrom = chunkTo.AddDays(1);
            }
        }
        finally
        {
            // Chunks saved before a failure are still loaded data and belong in the report.
            AddLoaded(loaded, pair, loadedFrom, loadedTo);
        }
    }

    /// <summary>
    /// Loads [<paramref name="requiredFrom"/>; <paramref name="leftEnd"/>] from new dates to old. For a pair
    /// without coverage the first chunk creates it; an empty first chunk means the service has no data
    /// for the currency at all (nothing is stored, the next pass asks again). Without that, an empty
    /// chunk means the data starts later: the left border is reached.
    /// </summary>
    private async Task LoadLeftEdgeAsync(
        Pair pair, HistoricalRateCoverage? coverage, DateOnly requiredFrom, DateOnly leftEnd, int chunkDays,
        List<HistoryRangeLoaded> loaded, CancellationToken cancellationToken)
    {
        DateOnly? loadedFrom = null;
        DateOnly? loadedTo = null;

        try
        {
            while (leftEnd >= requiredFrom)
            {
                var chunkFrom = Later(requiredFrom, leftEnd.AddDays(-(chunkDays - 1)));
                var rates = await frankfurterClient.GetRatesForPeriodAsync(pair.FromCode, pair.ToCode, chunkFrom, leftEnd, cancellationToken);
                var now = DateTimeOffset.UtcNow;

                if (rates.Count == 0)
                {
                    if (coverage is not null)
                    {
                        coverage.ExtendLeft(requiredFrom, now);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    break;
                }

                await UpsertAsync(pair, rates, chunkFrom, leftEnd, now, cancellationToken);
                if (coverage is null)
                {
                    coverage = HistoricalRateCoverage.Create(pair.FromId, pair.ToId, chunkFrom, leftEnd, now);
                    coverageRepository.Add(coverage);
                }
                else
                {
                    coverage.ExtendLeft(chunkFrom, now);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                loadedTo ??= leftEnd;
                loadedFrom = chunkFrom;
                leftEnd = chunkFrom.AddDays(-1);
            }
        }
        finally
        {
            AddLoaded(loaded, pair, loadedFrom, loadedTo);
        }
    }

    private static void AddLoaded(List<HistoryRangeLoaded> loaded, Pair pair, DateOnly? rangeFrom, DateOnly? rangeTo)
    {
        if (rangeFrom is { } from && rangeTo is { } to)
        {
            loaded.Add(new HistoryRangeLoaded(pair.FromId, pair.ToId, from, to));
        }
    }

    /// <summary>Inserts new dates, replaces the rate of stored dates whose rate changed; days outside the chunk are ignored.</summary>
    private async Task UpsertAsync(
        Pair pair, IReadOnlyList<(DateOnly Date, decimal Rate)> rates, DateOnly chunkFrom, DateOnly chunkTo,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = (await historyRepository.GetRangeAsync(pair.FromId, pair.ToId, chunkFrom, chunkTo, cancellationToken))
            .ToDictionary(r => r.RateDate);

        // One row per date; the last element wins if the service repeats a date.
        foreach (var (date, rate) in rates.Where(r => r.Date >= chunkFrom && r.Date <= chunkTo).GroupBy(r => r.Date).Select(g => g.Last()))
        {
            var value = new ExchangeRateValue(rate);
            if (existing.TryGetValue(date, out var stored))
            {
                if (stored.Rate.Rate != rate)
                {
                    stored.UpdateRate(value, now);
                }
            }
            else
            {
                historyRepository.Add(HistoricalExchangeRate.Create(pair.FromId, pair.ToId, date, value, now));
            }
        }
    }

    private static DateOnly Earlier(DateOnly a, DateOnly b) => a <= b ? a : b;

    private static DateOnly Later(DateOnly a, DateOnly b) => a >= b ? a : b;

    private readonly record struct Pair(CurrencyId FromId, string FromCode, CurrencyId ToId, string ToCode);
}
