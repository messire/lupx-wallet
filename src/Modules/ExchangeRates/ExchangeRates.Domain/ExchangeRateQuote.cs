using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Агрегат ExchangeRateQuote (ddd-model.md, §2.7) — "последний известный" курс валютной
/// пары, идентифицируется парой (FromCurrencyId, ToCurrencyId), без суррогатного Id (по
/// аналогии с BalanceHistory.Domain.BalanceSnapshot). Курсы "на дату" — отдельная сущность
/// <see cref="HistoricalExchangeRate"/> (append-only, та же концептуальная граница
/// агрегата ddd-model.md: "один курс одной пары на один момент/дату").
///
/// Не реализует IHasDomainEvents/AggregateRoot&lt;TId&gt;: события RefreshExchangeRates
/// (ExchangeRatesUpdated/ExchangeRateUpdateFailed) описывают результат прогона обновления
/// в целом — сразу несколько пар за один вызов, часть из которых может завершиться ошибкой
/// без единой строки в БД для события-отказа — поэтому публикуются напрямую из
/// RefreshExchangeRatesCommandHandler через IDomainEventDispatcher, а не через обычный
/// путь "EF SaveChanges-перехватчик читает DomainEvents с отслеживаемого агрегата"
/// (см. комментарий в ExchangeRates.Application/RefreshExchangeRatesCommand.cs).
/// </summary>
public sealed class ExchangeRateQuote
{
    private decimal _rate;

    public CurrencyId FromCurrencyId { get; private set; }
    public CurrencyId ToCurrencyId { get; private set; }
    public ExchangeRateValue Rate => new(_rate);
    public DateTimeOffset FetchedAt { get; private set; }

    private ExchangeRateQuote()
    {
        // Только для EF Core.
    }

    public static ExchangeRateQuote Create(CurrencyId from, CurrencyId to, ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        EnsureDifferentCurrencies(from, to);
        return new ExchangeRateQuote
        {
            FromCurrencyId = from,
            ToCurrencyId = to,
            _rate = rate.Rate,
            FetchedAt = fetchedAt,
        };
    }

    /// <summary>
    /// Раздел 2 бизнес-требований / ddd-model.md §5: Rate и FetchedAt меняются только при
    /// успешном ответе источника — при ошибке вызывающий код (RefreshExchangeRatesCommand)
    /// просто не вызывает этот метод для соответствующей пары.
    /// </summary>
    public void UpdateRate(ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        _rate = rate.Rate;
        FetchedAt = fetchedAt;
    }

    private static void EnsureDifferentCurrencies(CurrencyId from, CurrencyId to)
    {
        if (from == to)
        {
            throw new SameCurrencyExchangeRateException(from.Value);
        }
    }
}
