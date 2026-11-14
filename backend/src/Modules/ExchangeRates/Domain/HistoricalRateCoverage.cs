using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Date range [CoveredFrom; CoveredTo] of <see cref="HistoricalExchangeRate"/> rows already
/// loaded for one currency pair (ADR-0004, §9). The range is contiguous and only grows, so an
/// interrupted history load resumes where it stopped and a repeated pass requests nothing that
/// is already covered. Identified by (FromCurrencyId, ToCurrencyId), no surrogate Id.
/// </summary>
public sealed class HistoricalRateCoverage
{
    public CurrencyId FromCurrencyId { get; private set; }
    public CurrencyId ToCurrencyId { get; private set; }
    public DateOnly CoveredFrom { get; private set; }
    public DateOnly CoveredTo { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private HistoricalRateCoverage()
    {
        // EF Core only.
    }

    public static HistoricalRateCoverage Create(
        CurrencyId from, CurrencyId to, DateOnly coveredFrom, DateOnly coveredTo, DateTimeOffset updatedAt)
    {
        if (from == to)
        {
            throw new SameCurrencyExchangeRateException(from.Value);
        }

        if (coveredFrom > coveredTo)
        {
            throw new InvalidCoverageRangeException(coveredFrom, coveredTo);
        }

        return new HistoricalRateCoverage
        {
            FromCurrencyId = from,
            ToCurrencyId = to,
            CoveredFrom = coveredFrom,
            CoveredTo = coveredTo,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Moves the left edge to <paramref name="newCoveredFrom"/>; the range never shrinks.</summary>
    public void ExtendLeft(DateOnly newCoveredFrom, DateTimeOffset updatedAt)
    {
        if (newCoveredFrom >= CoveredFrom)
        {
            return;
        }

        CoveredFrom = newCoveredFrom;
        UpdatedAt = updatedAt;
    }

    /// <summary>Moves the right edge to <paramref name="newCoveredTo"/>; the range never shrinks.</summary>
    public void ExtendRight(DateOnly newCoveredTo, DateTimeOffset updatedAt)
    {
        if (newCoveredTo <= CoveredTo)
        {
            return;
        }

        CoveredTo = newCoveredTo;
        UpdatedAt = updatedAt;
    }
}
