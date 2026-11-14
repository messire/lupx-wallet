using LupexWallet.ExchangeRates.Application;
using LupexWallet.Funds.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>One wallet's free funds (ADR-0010 §7, #12), in the wallet currency.</summary>
public sealed record WalletFreeFundsDto(
    Guid WalletId, Guid CurrencyId, bool IncludeInTotal, bool IsPrimary,
    decimal Balance, decimal Reserved, decimal Free, decimal Deficit);

/// <summary>
/// Free funds report (ADR-0010 §7, #12): per wallet and a summary over the "include in total"
/// wallets in the primary-wallet currency by the latest known rates. A wallet without a rate is
/// left out of the summary with a reason (same rule as the total amount, ADR-0004 §9).
/// </summary>
public sealed record FreeFundsDto(
    Guid CurrencyId,
    decimal ActualAmount,
    decimal ReservedAmount,
    decimal FreeAmount,
    decimal DeficitAmount,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateDto> Rates,
    IReadOnlyList<ExcludedWalletDto> ExcludedWallets,
    IReadOnlyList<WalletFreeFundsDto> Wallets);

/// <summary>GET /reporting/free-funds. Null — no primary wallet yet (404, like the total amount).</summary>
public sealed record GetFreeFundsQuery : IRequest<FreeFundsDto?>, IQuery<FreeFundsDto?>;

public sealed class GetFreeFundsQueryHandler(
    IWalletTotalsSource walletTotalsSource,
    IFundReservationsLookup fundReservationsLookup,
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<GetFreeFundsQuery, FreeFundsDto?>
{
    public async Task<FreeFundsDto?> Handle(GetFreeFundsQuery request, CancellationToken cancellationToken)
    {
        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        var primary = wallets.FirstOrDefault(w => w.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        var reservedByWallet = (await fundReservationsLookup.GetReservedByWalletAsync(cancellationToken))
            .ToDictionary(r => r.WalletId, r => r.Reserved);
        var converter = new LatestRateConverter(exchangeRateLookup, currencyLookup, primary.CurrencyId);

        var rows = wallets
            .Select(w => FreeFundsCalculator.ForWallet(w, reservedByWallet.GetValueOrDefault(w.WalletId)))
            .ToList();

        var summary = new FreeFundsCalculator.Summary();
        var excluded = new List<ExcludedWalletDto>();
        foreach (var row in rows.Where(r => r.IncludeInTotal))
        {
            var rate = await converter.RateAsync(new CurrencyId(row.CurrencyId), cancellationToken);
            if (rate is null)
            {
                excluded.Add(new ExcludedWalletDto(row.WalletId, await converter.NoRateReasonAsync(new CurrencyId(row.CurrencyId), cancellationToken)));
                continue;
            }

            summary.Add(row, rate.Value);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new FreeFundsDto(
            primary.CurrencyId.Value,
            summary.Actual,
            summary.Reserved,
            summary.Free,
            summary.Deficit,
            converter.RatesAsOfDate(today),
            LatestRateConverter.Source,
            converter.AppliedRates,
            excluded,
            rows.OrderByDescending(r => r.IsPrimary).ToList());
    }
}

/// <summary>Pure free-funds arithmetic (ADR-0010 §4 B7, §7).</summary>
public static class FreeFundsCalculator
{
    public static WalletFreeFundsDto ForWallet(WalletTotalInfo wallet, decimal reserved) => new(
        wallet.WalletId.Value,
        wallet.CurrencyId.Value,
        wallet.IncludeInTotal,
        wallet.IsPrimary,
        wallet.CurrentBalance,
        reserved,
        ReservationCoverage.Free(wallet.CurrentBalance, reserved),
        ReservationCoverage.WalletDeficit(wallet.CurrentBalance, reserved));

    /// <summary>Converted sums; Free = Actual − Reserved exactly, so the headline total stays consistent.</summary>
    public sealed class Summary
    {
        public decimal Actual { get; private set; }
        public decimal Reserved { get; private set; }
        public decimal Deficit { get; private set; }
        public decimal Free => Actual - Reserved;

        public void Add(WalletFreeFundsDto wallet, decimal rate)
        {
            Actual += wallet.Balance * rate;
            Reserved += wallet.Reserved * rate;
            Deficit += wallet.Deficit * rate;
        }
    }
}
