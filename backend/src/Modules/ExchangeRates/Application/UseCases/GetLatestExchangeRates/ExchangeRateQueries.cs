using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.ExchangeRates.Application;

public sealed record ExchangeRateQuoteDto(Guid FromCurrencyId, Guid ToCurrencyId, decimal Rate);

public sealed record LatestExchangeRatesDto(DateTimeOffset? LastSuccessfulUpdate, IReadOnlyList<ExchangeRateQuoteDto> Rates);

/// <summary>GET /exchange-rates/latest (UC-08, UC-10) — returns an empty list, not 404/500, before the first successful refresh.</summary>
public sealed record GetLatestExchangeRatesQuery : IRequest<LatestExchangeRatesDto>, IQuery<LatestExchangeRatesDto>;

public sealed class GetLatestExchangeRatesQueryHandler(
    ILatestExchangeRateRepository repository, IWalletCurrencySet walletCurrencySet)
    : IRequestHandler<GetLatestExchangeRatesQuery, LatestExchangeRatesDto>
{
    public async Task<LatestExchangeRatesDto> Handle(GetLatestExchangeRatesQuery request, CancellationToken cancellationToken)
    {
        var allQuotes = await repository.GetAllAsync(cancellationToken);

        // M5: openapi.yaml — "курсы к текущей основной валюте". RefreshExchangeRatesCommandHandler
        // уже удаляет пары к прежней основной валюте при обновлении, но этот фильтр — страховка
        // на чтение на случай, если обновление ещё не произошло после смены основного кошелька
        // (окно между PrimaryWalletChanged и фоновым RefreshExchangeRatesCommand, ADR-0011).
        var currencySet = await walletCurrencySet.GetAsync(cancellationToken);
        var quotes = currencySet.PrimaryCurrencyId is { } primaryCurrencyId
            ? allQuotes.Where(q => q.ToCurrencyId == primaryCurrencyId).ToList()
            : [];

        var lastSuccessfulUpdate = quotes.Count > 0 ? quotes.Max(q => q.FetchedAt) : (DateTimeOffset?)null;

        return new LatestExchangeRatesDto(
            lastSuccessfulUpdate,
            quotes.Select(q => new ExchangeRateQuoteDto(q.FromCurrencyId.Value, q.ToCurrencyId.Value, q.Rate.Rate)).ToList());
    }
}
