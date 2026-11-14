using LupexWallet.Debts.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>Remaining debt to one creditor, converted to the primary currency.</summary>
public sealed record CreditorDebtDto(Guid CreditorId, string CreditorName, decimal Amount, int DebtCount);

/// <summary>One open debt or loan; ConvertedAmount is null when its currency has no rate.</summary>
public sealed record ObligationRemainingDto(
    Guid Id, string Name, Guid CreditorId, string CreditorName, Guid CurrencyId, decimal RemainingAmount, decimal? ConvertedAmount);

/// <summary>
/// Debts summary in the primary-wallet currency (ADR-0010 §3, §7): total debt and breakdown by
/// creditor, loans remaining as a separate block. Latest known rates; items without a rate are left
/// out of the totals and listed in MissingRates.
/// </summary>
public sealed record DebtsSummaryReportDto(
    Guid CurrencyId,
    decimal TotalDebtAmount,
    int DebtCount,
    IReadOnlyList<CreditorDebtDto> Creditors,
    IReadOnlyList<ObligationRemainingDto> Debts,
    decimal TotalLoanRemainingAmount,
    int LoanCount,
    IReadOnlyList<ObligationRemainingDto> Loans,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateDto> Rates,
    IReadOnlyList<MissingRateDto> MissingRates);

/// <summary>GET /reporting/debts-summary. Null — no primary wallet yet.</summary>
public sealed record GetDebtsSummaryQuery : IRequest<DebtsSummaryReportDto?>, IQuery<DebtsSummaryReportDto?>;

public sealed class GetDebtsSummaryQueryHandler(
    IWalletTotalsSource walletTotalsSource,
    IDebtObligationsLookup debtObligationsLookup,
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<GetDebtsSummaryQuery, DebtsSummaryReportDto?>
{
    public async Task<DebtsSummaryReportDto?> Handle(GetDebtsSummaryQuery request, CancellationToken cancellationToken)
    {
        var primary = (await walletTotalsSource.GetAllAsync(cancellationToken)).FirstOrDefault(w => w.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        var converter = new LatestRateConverter(exchangeRateLookup, currencyLookup, primary.CurrencyId);
        var obligations = await debtObligationsLookup.ListOpenObligationsAsync(cancellationToken);

        var debts = new List<ObligationRemainingDto>();
        var loans = new List<ObligationRemainingDto>();
        foreach (var o in obligations)
        {
            var dto = new ObligationRemainingDto(
                o.Id, o.Name, o.CreditorId, o.CreditorName, o.CurrencyId.Value, o.RemainingAmount,
                await converter.ConvertAsync(o.RemainingAmount, o.CurrencyId, cancellationToken));
            (o.Kind == ObligationKind.Debt ? debts : loans).Add(dto);
        }

        var creditors = debts
            .Where(d => d.ConvertedAmount is not null)
            .GroupBy(d => d.CreditorId)
            .Select(g => new CreditorDebtDto(g.Key, g.First().CreditorName, g.Sum(d => d.ConvertedAmount!.Value), g.Count()))
            .OrderByDescending(c => c.Amount).ThenBy(c => c.CreditorName)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new DebtsSummaryReportDto(
            primary.CurrencyId.Value,
            debts.Sum(d => d.ConvertedAmount ?? 0m),
            debts.Count,
            creditors,
            debts.OrderByDescending(d => d.ConvertedAmount ?? 0m).ThenBy(d => d.Name).ToList(),
            loans.Sum(l => l.ConvertedAmount ?? 0m),
            loans.Count,
            loans.OrderByDescending(l => l.ConvertedAmount ?? 0m).ThenBy(l => l.Name).ToList(),
            converter.RatesAsOfDate(today),
            LatestRateConverter.Source,
            converter.AppliedRates,
            await converter.MissingRatesAsync(cancellationToken));
    }
}
