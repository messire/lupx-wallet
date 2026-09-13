using LupexWallet.BalanceHistory.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>A wallet excluded from the total — risk #6 (docs/PROGRESS.md, item 5.1): user decision,
/// happy path (not 409, not a 1:1 rate) — the wallet is skipped and the reason is returned to the client.</summary>
public sealed record ExcludedWalletDto(Guid WalletId, string Reason);

/// <summary>docs/api/openapi.yaml, TotalAmount schema (Money is expanded into Amount+CurrencyId
/// here and reassembled in Reporting.Api, the same way WalletDto does for Wallet.CurrentBalance).</summary>
public sealed record TotalAmountDto(
    DateOnly? Date,
    decimal Amount,
    Guid CurrencyId,
    DateOnly RatesAsOfDate,
    IReadOnlyList<ExcludedWalletDto> ExcludedWallets);

/// <summary>
/// GET /api/v1/reporting/total-amount. No Date — current total (UC-19); with Date —
/// historical (UC-21). Null means no wallet exists yet in the system (no primary wallet,
/// hence no display currency) — Reporting.Api turns this into 404, the same approach
/// GetWalletQuery uses for a non-existent wallet.
/// </summary>
public sealed record GetTotalAmountQuery(DateOnly? Date) : IRequest<TotalAmountDto?>, IQuery<TotalAmountDto?>;

public sealed class GetTotalAmountQueryHandler(
    IWalletTotalsSource walletTotalsSource,
    IWalletBalanceOnDateLookup balanceOnDateLookup,
    IExchangeRateLookup exchangeRateLookup) : IRequestHandler<GetTotalAmountQuery, TotalAmountDto?>
{
    public async Task<TotalAmountDto?> Handle(GetTotalAmountQuery request, CancellationToken cancellationToken)
    {
        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        var primary = wallets.FirstOrDefault(w => w.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        var targetCurrencyId = primary.CurrencyId;
        var total = 0m;
        var excludedWallets = new List<ExcludedWalletDto>();

        // Несколько разных фактических дат курса возможны только при нескольких разных
        // не-основных валютах с историческим fallback (GetOrFetchHistoricalRateAsync может
        // вернуть более раннюю дату, чем запрошенная) — ratesAsOfDate в ответе один на весь
        // расчет (openapi.yaml: TotalAmount.ratesAsOfDate — одиночное поле), поэтому берем
        // самую раннюю из фактически примененных дат (наиболее консервативная оценка
        // "насколько устарели данные", а не самую позднюю, которая скрыла бы устаревший курс).
        DateOnly? earliestAppliedRateDate = null;

        foreach (var wallet in wallets.Where(w => w.IncludeInTotal))
        {
            decimal walletAmount;
            if (request.Date is { } date)
            {
                var balance = await balanceOnDateLookup.GetBalanceOnDateAsync(wallet.WalletId, date, cancellationToken);
                if (balance is null)
                {
                    // Не ожидается в норме (кошелек уже перечислен через IWalletTotalsSource),
                    // но исключаем с явной причиной вместо падения — на случай рассинхронизации
                    // между модулями Wallets/BalanceHistory.
                    excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, "Не удалось получить баланс кошелька на указанную дату."));
                    continue;
                }

                walletAmount = balance.Value;
            }
            else
            {
                walletAmount = wallet.CurrentBalance;
            }

            if (wallet.CurrencyId == targetCurrencyId)
            {
                // DoD п.2: кошелек уже в валюте основного — без похода в ExchangeRates, курс 1:1.
                total += walletAmount;
                continue;
            }

            decimal rate;
            DateOnly appliedRateDate;
            if (request.Date is { } historicalDate)
            {
                var rateResult = await exchangeRateLookup.GetOrFetchHistoricalRateAsync(
                    wallet.CurrencyId, targetCurrencyId, historicalDate, cancellationToken);
                if (rateResult is null)
                {
                    excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, NoRateReason(wallet.CurrencyId, historicalDate)));
                    continue;
                }

                (rate, appliedRateDate) = rateResult.Value;
            }
            else
            {
                var latestRate = await exchangeRateLookup.GetRateAsync(wallet.CurrencyId, targetCurrencyId, cancellationToken);
                if (latestRate is null)
                {
                    excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, NoRateReason(wallet.CurrencyId, null)));
                    continue;
                }

                rate = latestRate.Value;
                // GetRateAsync отдает только "последний известный курс" без собственной даты
                // (ExchangeRates.Application.IExchangeRateLookup) — для текущей суммы это
                // сегодняшний день (курсы актуальны на момент запроса, UC-19).
                appliedRateDate = DateOnly.FromDateTime(DateTime.UtcNow);
            }

            total += walletAmount * rate;
            earliestAppliedRateDate = earliestAppliedRateDate is null || appliedRateDate < earliestAppliedRateDate
                ? appliedRateDate
                : earliestAppliedRateDate;
        }

        var ratesAsOfDate = earliestAppliedRateDate ?? request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        return new TotalAmountDto(request.Date, total, targetCurrencyId.Value, ratesAsOfDate, excludedWallets);
    }

    private static string NoRateReason(CurrencyId currencyId, DateOnly? date) => date is { } d
        ? $"Курс валюты {currencyId.Value} на дату {d:yyyy-MM-dd} недоступен."
        : $"Курс валюты {currencyId.Value} недоступен.";
}
