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

/// <summary>
/// Handler for RefreshExchangeRatesCommand (see the comment in
/// ExchangeRates.Application/RefreshExchangeRatesCommand.cs — lives in Infrastructure, not
/// Application, because of the IDomainEventDispatcher dependency). Registered manually in
/// ExchangeRatesModuleExtensions (not via MediatR assembly scanning in Program.cs, which
/// only looks at *.Application assemblies — the same approach as
/// BalanceHistory.Infrastructure.OperationEventHandlers).
/// </summary>
public sealed class RefreshExchangeRatesCommandHandler(
    ILatestExchangeRateRepository repository,
    IExchangeRatesUnitOfWork unitOfWork,
    IFrankfurterClient frankfurterClient,
    IWalletCurrencySet walletCurrencySet,
    ICurrencyLookup currencyLookup,
    IExchangeRateRefreshSignal refreshSignal,
    IDomainEventDispatcher eventDispatcher,
    ILogger<RefreshExchangeRatesCommandHandler> logger)
    : IRequestHandler<RefreshExchangeRatesCommand, RefreshExchangeRatesResult>
{
    public async Task<RefreshExchangeRatesResult> Handle(RefreshExchangeRatesCommand request, CancellationToken cancellationToken)
    {
        var currencySet = await walletCurrencySet.GetAsync(cancellationToken);
        var failedPairs = new List<ExchangeRatePairFailure>();

        if (currencySet.PrimaryCurrencyId is { } primaryCurrencyId)
        {
            var updatedPairs = new List<ExchangeRatePairResult>();
            var now = DateTimeOffset.UtcNow;

            var catalogCurrencies = await currencyLookup.GetAllAsync(cancellationToken);
            var pairsToRefresh = catalogCurrencies
                .Select(currency => currency.Id)
                .Where(currencyId => currencyId != primaryCurrencyId)
                .Distinct()
                .ToList();

            foreach (var fromCurrencyId in pairsToRefresh)
            {
                await RefreshPairAsync(fromCurrencyId, primaryCurrencyId, now, updatedPairs, failedPairs, cancellationToken);
            }

            // A currency can be deleted while the run is in flight (ADR-0002): re-read the catalog and
            // drop quotes about to be inserted for vanished currencies before they become orphans.
            var knownCurrencyIds = (await currencyLookup.GetAllAsync(cancellationToken))
                .Select(currency => currency.Id)
                .ToHashSet();
            foreach (var pair in updatedPairs.Where(p => !knownCurrencyIds.Contains(p.FromCurrencyId)).ToList())
            {
                // Remove resolves the tracked instance (pending insert or update) of the same pair by key.
                repository.Remove(ExchangeRateQuote.Create(
                    pair.FromCurrencyId, pair.ToCurrencyId, new ExchangeRateValue(pair.Rate), now));
                updatedPairs.Remove(pair);
            }

            // Quotes that GET /exchange-rates/latest must not serve: not targeting the current primary
            // currency (openapi.yaml) or referencing a currency absent from the catalog. The read-side
            // filter is a safety net; here the stale data itself is cleaned up.
            var staleQuotes = (await repository.GetAllAsync(cancellationToken))
                .Where(q => q.ToCurrencyId != primaryCurrencyId
                    || !knownCurrencyIds.Contains(q.FromCurrencyId)
                    || !knownCurrencyIds.Contains(q.ToCurrencyId))
                .ToList();
            foreach (var staleQuote in staleQuotes)
            {
                repository.Remove(staleQuote);
            }

            await SaveSkippingConflictsAsync(updatedPairs, cancellationToken);

            if (updatedPairs.Count > 0)
            {
                await eventDispatcher.DispatchAsync([new ExchangeRatesUpdated(now, updatedPairs)], cancellationToken);
            }

            if (failedPairs.Count > 0)
            {
                await eventDispatcher.DispatchAsync([new ExchangeRateUpdateFailed(now, failedPairs)], cancellationToken);
            }
        }

        if (request.IsManualTrigger)
        {
            // Сам ручной прогон уже выполнен выше (или пропущен — нет основного кошелька) —
            // фоновому сервису остаётся только перезапустить суточный отсчёт (ADR-0004 п.3).
            refreshSignal.NotifyManualRefreshCompleted();
        }

        var allLatest = await repository.GetAllAsync(cancellationToken);
        var lastSuccessfulUpdate = allLatest.Count > 0 ? allLatest.Max(q => q.FetchedAt) : (DateTimeOffset?)null;

        return new RefreshExchangeRatesResult(
            lastSuccessfulUpdate,
            allLatest.Select(ToDto).ToList(),
            HadFailures: failedPairs.Count > 0);
    }

    /// <summary>
    /// Saves the run; a row that vanished concurrently (currency deletion) makes UPDATE/DELETE touch 0 rows
    /// and raises DbUpdateConcurrencyException. Such quotes are detached and skipped, the rest is saved.
    /// </summary>
    private async Task SaveSkippingConflictsAsync(List<ExchangeRatePairResult> updatedPairs, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is ExchangeRateQuote))
            {
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                    if (entry.Entity is ExchangeRateQuote quote)
                    {
                        logger.LogWarning(
                            ex, "Quote {From}->{To} was removed concurrently and is skipped", quote.FromCurrencyId, quote.ToCurrencyId);
                        updatedPairs.RemoveAll(p => p.FromCurrencyId == quote.FromCurrencyId && p.ToCurrencyId == quote.ToCurrencyId);
                    }
                }
            }
        }
    }

    private async Task RefreshPairAsync(
        CurrencyId fromCurrencyId,
        CurrencyId primaryCurrencyId,
        DateTimeOffset now,
        List<ExchangeRatePairResult> updatedPairs,
        List<ExchangeRatePairFailure> failedPairs,
        CancellationToken cancellationToken)
    {
        var fromCurrency = await currencyLookup.GetAsync(fromCurrencyId, cancellationToken);
        var toCurrency = await currencyLookup.GetAsync(primaryCurrencyId, cancellationToken);
        if (fromCurrency is null || toCurrency is null)
        {
            logger.LogWarning(
                "Пропущена пара {From}->{To}: валюта не найдена в справочнике", fromCurrencyId, primaryCurrencyId);
            return;
        }

        try
        {
            var rate = await frankfurterClient.GetLatestRateAsync(fromCurrency.Code, toCurrency.Code, cancellationToken);
            var rateValue = new ExchangeRateValue(rate);

            var existing = await repository.FindAsync(fromCurrencyId, primaryCurrencyId, cancellationToken);
            if (existing is null)
            {
                repository.Add(ExchangeRateQuote.Create(fromCurrencyId, primaryCurrencyId, rateValue, now));
            }
            else
            {
                existing.UpdateRate(rateValue, now);
            }

            updatedPairs.Add(new ExchangeRatePairResult(fromCurrencyId, primaryCurrencyId, rate));
        }
        catch (FrankfurterUnsupportedCurrencyException ex)
        {
            // Permanent property of the currency, not a transient source failure: skip silently,
            // do not escalate to 502/ExchangeRateUpdateFailed (ADR-0004, "Consequences").
            logger.LogInformation(
                ex, "Валюта не поддерживается Frankfurter — пара {From}->{To} пропущена", fromCurrency.Code, toCurrency.Code);
        }
        catch (FrankfurterUnavailableException ex)
        {
            // Временная недоступность источника — прежний курс сохраняется без изменений
            // (ADR-0004 п.6), fetched_at не трогаем.
            logger.LogWarning(ex, "Frankfurter недоступен для пары {From}->{To}", fromCurrency.Code, toCurrency.Code);
            failedPairs.Add(new ExchangeRatePairFailure(fromCurrencyId, primaryCurrencyId, ex.Message));
        }
    }

    private static ExchangeRateQuoteDto ToDto(ExchangeRateQuote quote) =>
        new(quote.FromCurrencyId.Value, quote.ToCurrencyId.Value, quote.Rate.Rate);
}
