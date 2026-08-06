using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Обработчик RefreshExchangeRatesCommand (см. комментарий в
/// ExchangeRates.Application/RefreshExchangeRatesCommand.cs — живёт в Infrastructure, а не
/// в Application, из-за зависимости на IDomainEventDispatcher). Регистрируется вручную в
/// ExchangeRatesModuleExtensions (не через сканирование сборки MediatR в Program.cs, которое
/// смотрит только *.Application-сборки — тот же приём, что и
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

            var pairsToRefresh = currencySet.AllWalletCurrencyIds
                .Where(currencyId => currencyId != primaryCurrencyId)
                .Distinct()
                .ToList();

            foreach (var fromCurrencyId in pairsToRefresh)
            {
                await RefreshPairAsync(fromCurrencyId, primaryCurrencyId, now, updatedPairs, failedPairs, cancellationToken);
            }

            // M5: после смены основного кошелька старые пары "валюта -> прежняя основная"
            // становятся неактуальными — GET /exchange-rates/latest должен отдавать курсы
            // только к текущей основной валюте (openapi.yaml). Читающий фильтр в
            // GetLatestExchangeRatesQueryHandler — страховка; здесь же чистим сами данные,
            // чтобы устаревшие пары не копились бесконечно.
            var staleQuotes = (await repository.GetAllAsync(cancellationToken))
                .Where(q => q.ToCurrencyId != primaryCurrencyId)
                .ToList();
            foreach (var staleQuote in staleQuotes)
            {
                repository.Remove(staleQuote);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);

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
            // фоновому сервису остаётся только перезапустить суточный отсчёт (ADR-0001 п.3).
            refreshSignal.NotifyManualRefreshCompleted();
        }

        var allLatest = await repository.GetAllAsync(cancellationToken);
        var lastSuccessfulUpdate = allLatest.Count > 0 ? allLatest.Max(q => q.FetchedAt) : (DateTimeOffset?)null;

        return new RefreshExchangeRatesResult(
            lastSuccessfulUpdate,
            allLatest.Select(ToDto).ToList(),
            HadFailures: failedPairs.Count > 0);
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
            // Постоянное свойство валюты — не временный сбой источника, пропускаем молча,
            // не эскалируем до 502/ExchangeRateUpdateFailed (ADR-0001, "Последствия";
            // docs/PROGRESS.md, W1.1 DoD п.6).
            logger.LogInformation(
                ex, "Валюта не поддерживается Frankfurter — пара {From}->{To} пропущена", fromCurrency.Code, toCurrency.Code);
        }
        catch (FrankfurterUnavailableException ex)
        {
            // Временная недоступность источника — прежний курс сохраняется без изменений
            // (ADR-0001 п.6), fetched_at не трогаем.
            logger.LogWarning(ex, "Frankfurter недоступен для пары {From}->{To}", fromCurrency.Code, toCurrency.Code);
            failedPairs.Add(new ExchangeRatePairFailure(fromCurrencyId, primaryCurrencyId, ex.Message));
        }
    }

    private static ExchangeRateQuoteDto ToDto(ExchangeRateQuote quote) =>
        new(quote.FromCurrencyId.Value, quote.ToCurrencyId.Value, quote.Rate.Rate);
}
