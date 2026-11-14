using LupexWallet.BalanceHistory.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Funds.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>A wallet excluded from the total (ADR-0004 §9): user decision, happy path (not 409,
/// not a 1:1 rate) — the wallet is skipped and the reason is returned to the client.</summary>
public sealed record ExcludedWalletDto(Guid WalletId, string Reason);

/// <summary>
/// docs/api/openapi.yaml, TotalAmount schema (Money is expanded into Amount+CurrencyId
/// here and reassembled in Reporting.Api, the same way WalletDto does for Wallet.CurrentBalance).
/// ADR-0010 §7, B9: the current total is the free amount (reserves of active funds reduce it), so
/// <see cref="Amount"/> = <see cref="FreeAmount"/>; the actual balance and the reserve are shown next to it.
/// Reserves have no history, so a historical total has no reserve: Amount = ActualAmount, Reserved/Free are null.
/// </summary>
public sealed record TotalAmountDto(
    DateOnly? Date,
    decimal Amount,
    Guid CurrencyId,
    DateOnly RatesAsOfDate,
    IReadOnlyList<ExcludedWalletDto> ExcludedWallets,
    decimal ActualAmount,
    decimal? ReservedAmount,
    decimal? FreeAmount);

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
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup,
    IFundReservationsLookup fundReservationsLookup) : IRequestHandler<GetTotalAmountQuery, TotalAmountDto?>
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
        var reservedTotal = 0m;
        var excludedWallets = new List<ExcludedWalletDto>();

        // Reserves exist only now (no history): a historical total is the actual balance only.
        var reservedByWallet = request.Date is null
            ? (await fundReservationsLookup.GetReservedByWalletAsync(cancellationToken)).ToDictionary(r => r.WalletId, r => r.Reserved)
            : [];

        // Несколько разных фактических дат курса возможны только при нескольких разных
        // не-основных валютах (GetHistoricalRateAsync возвращает ближайший более ранний курс,
        // если на запрошенную дату курса нет) — ratesAsOfDate в ответе один на весь
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
                // Кошелек уже в валюте основного — без похода в ExchangeRates, курс 1:1.
                total += walletAmount;
                reservedTotal += reservedByWallet.GetValueOrDefault(wallet.WalletId);
                continue;
            }

            decimal rate;
            DateOnly appliedRateDate;
            if (request.Date is { } historicalDate)
            {
                var rateResult = await exchangeRateLookup.GetHistoricalRateAsync(
                    wallet.CurrencyId, targetCurrencyId, historicalDate, cancellationToken);
                if (rateResult is null)
                {
                    excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, await NoRateReasonAsync(wallet.CurrencyId, historicalDate, cancellationToken)));
                    continue;
                }

                (rate, appliedRateDate) = rateResult.Value;
            }
            else
            {
                var latestRate = await exchangeRateLookup.GetLatestRateWithDateAsync(wallet.CurrencyId, targetCurrencyId, cancellationToken);
                if (latestRate is null)
                {
                    excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, await NoRateReasonAsync(wallet.CurrencyId, null, cancellationToken)));
                    continue;
                }

                // The actual date of the latest known rate, as free-funds and the forecast report it
                // (LatestRateConverter): total-amount.amount = free-funds.freeAmount, so the dates match too
                // (ADR-0010 §7; review v0.4.0, R9). A failed refresh shows the stale date, not today.
                (rate, appliedRateDate) = latestRate.Value;
            }

            total += walletAmount * rate;
            reservedTotal += reservedByWallet.GetValueOrDefault(wallet.WalletId) * rate;
            earliestAppliedRateDate = earliestAppliedRateDate is null || appliedRateDate < earliestAppliedRateDate
                ? appliedRateDate
                : earliestAppliedRateDate;
        }

        var ratesAsOfDate = earliestAppliedRateDate ?? request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        if (request.Date is not null)
        {
            return new TotalAmountDto(request.Date, total, targetCurrencyId.Value, ratesAsOfDate, excludedWallets, total, null, null);
        }

        var free = total - reservedTotal;
        return new TotalAmountDto(null, free, targetCurrencyId.Value, ratesAsOfDate, excludedWallets, total, reservedTotal, free);
    }

    private async Task<string> NoRateReasonAsync(CurrencyId currencyId, DateOnly? date, CancellationToken cancellationToken)
    {
        // The reason is shown to the user: use the currency code, falling back to the id only if the lookup misses.
        var currency = (await currencyLookup.GetAsync(currencyId, cancellationToken))?.Code ?? currencyId.Value.ToString();
        return date is { } d
            ? $"Курс валюты {currency} на дату {d:yyyy-MM-dd} недоступен."
            : $"Курс валюты {currency} недоступен.";
    }
}
