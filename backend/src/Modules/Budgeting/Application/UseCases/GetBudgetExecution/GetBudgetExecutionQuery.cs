using LupexWallet.Budgeting.Domain;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Budgeting.Application;

public sealed record CategoryExecutionDto(
    Guid ExpenseCategoryId, string Name, bool IsActive, decimal Limit, decimal Actual, decimal Remaining, bool IsOverspent);

/// <param name="Rate">Applied multiplier from <paramref name="FromCurrencyId"/> to the budget currency.</param>
/// <param name="IsFallback">No rate on the operation date; the closest earlier one was used.</param>
public sealed record AppliedRateDto(Guid FromCurrencyId, DateOnly OperationDate, DateOnly RateDate, decimal Rate, bool IsFallback);

/// <summary>An expense left out of the execution because no rate to the budget currency is known on or before its date.</summary>
public sealed record ExcludedOperationDto(Guid OperationId, Guid CurrencyId, DateOnly OperationDate, decimal Amount, string Reason);

/// <param name="UnbudgetedActual">"Без бюджета": expenses whose type is in no category with a limit in this budget.</param>
/// <param name="TotalBudgetedActual">Sum of <see cref="CategoryExecutionDto.Actual"/>.</param>
/// <param name="TotalActual">All counted expenses of the month: budgeted plus unbudgeted.</param>
public sealed record BudgetExecutionDto(
    Guid BudgetId,
    string Month,
    Guid CurrencyId,
    IReadOnlyList<CategoryExecutionDto> Categories,
    decimal UnbudgetedActual,
    decimal TotalLimit,
    decimal TotalBudgetedActual,
    decimal TotalRemaining,
    decimal TotalActual,
    string RateSource,
    IReadOnlyList<AppliedRateDto> AppliedRates,
    IReadOnlyList<ExcludedOperationDto> ExcludedOperations);

/// <summary>
/// Budget execution (ADR-0010 §5, B8): per limited category the sum of non-deleted expenses of its
/// operation types dated within the month, across all wallets. Another currency is converted by the
/// historical rate on the operation date, or the closest earlier one (ADR-0004 §8). Without any such
/// rate the operation is excluded and listed with a reason — the same rule as the total amount
/// (ADR-0004 §9). Null when the budget does not exist.
/// </summary>
public sealed record GetBudgetExecutionQuery(Guid BudgetId) : IRequest<BudgetExecutionDto?>, IQuery<BudgetExecutionDto?>;

public sealed class GetBudgetExecutionQueryHandler(
    IBudgetRepository budgetRepository,
    IExpenseCategoryRepository categoryRepository,
    IOperationFactsLookup operationFacts,
    IOperationTypeLookup operationTypeLookup,
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<GetBudgetExecutionQuery, BudgetExecutionDto?>
{
    /// <summary>The only rate source of the application (ADR-0004 §1).</summary>
    public const string RateSourceName = "Frankfurter";

    public async Task<BudgetExecutionDto?> Handle(GetBudgetExecutionQuery request, CancellationToken cancellationToken)
    {
        var budget = await budgetRepository.GetByIdAsync(new BudgetId(request.BudgetId), cancellationToken);
        if (budget is null)
        {
            return null;
        }

        var month = budget.BudgetMonth;
        var categories = await categoryRepository.GetAllAsync(cancellationToken);
        var limitedCategoryIds = budget.Limits.Select(l => l.ExpenseCategoryId).ToHashSet();
        var limitedCategoryByType = categories
            .Where(c => limitedCategoryIds.Contains(c.Id))
            .SelectMany(c => c.OperationTypeIds.Select(t => (TypeId: t, CategoryId: c.Id)))
            .ToDictionary(x => x.TypeId, x => x.CategoryId);

        var facts = await operationFacts.ListInRangeAsync(month.FirstDay, month.LastDay, cancellationToken);

        var isExpenseByType = new Dictionary<OperationTypeId, bool>();
        var rates = new Dictionary<(CurrencyId, DateOnly), AppliedRateDto?>();
        var actualByCategory = limitedCategoryIds.ToDictionary(id => id, _ => 0m);
        var unbudgeted = 0m;
        var excluded = new List<ExcludedOperationDto>();

        foreach (var fact in facts.OrderBy(f => f.OperationDate))
        {
            if (fact.IsDeleted || fact.IsCorrection || fact.IsTransferLeg || fact.OperationTypeId is not { } typeId)
            {
                continue;
            }

            if (!isExpenseByType.TryGetValue(typeId, out var isExpense))
            {
                var type = await operationTypeLookup.GetAsync(typeId, cancellationToken);
                isExpense = type?.BehaviorKindCode == ExpenseCategoryMembershipPolicy.ExpenseBehaviorCode;
                isExpenseByType[typeId] = isExpense;
            }

            if (!isExpense)
            {
                continue;
            }

            decimal amount;
            if (fact.CurrencyId == budget.CurrencyId)
            {
                amount = fact.Amount;
            }
            else
            {
                var key = (fact.CurrencyId, fact.OperationDate);
                if (!rates.TryGetValue(key, out var rate))
                {
                    rate = await FindRateAsync(fact.CurrencyId, budget.CurrencyId, fact.OperationDate, cancellationToken);
                    rates[key] = rate;
                }

                if (rate is null)
                {
                    excluded.Add(new ExcludedOperationDto(
                        fact.OperationId.Value, fact.CurrencyId.Value, fact.OperationDate, fact.Amount,
                        await NoRateReasonAsync(fact.CurrencyId, budget.CurrencyId, fact.OperationDate, cancellationToken)));
                    continue;
                }

                amount = fact.Amount * rate.Rate;
            }

            if (limitedCategoryByType.TryGetValue(typeId, out var categoryId))
            {
                actualByCategory[categoryId] += amount;
            }
            else
            {
                unbudgeted += amount;
            }
        }

        var categoryById = categories.ToDictionary(c => c.Id);
        var rows = budget.Limits
            .Select(limit =>
            {
                var category = categoryById.GetValueOrDefault(limit.ExpenseCategoryId);
                var actual = actualByCategory[limit.ExpenseCategoryId];
                var remaining = limit.Limit - actual;
                return new CategoryExecutionDto(
                    limit.ExpenseCategoryId.Value,
                    category?.Name ?? string.Empty,
                    category?.IsActive ?? false,
                    limit.Limit,
                    actual,
                    remaining,
                    remaining < 0);
            })
            .OrderBy(r => r.Name, StringComparer.CurrentCulture)
            .ToList();

        var totalLimit = rows.Sum(r => r.Limit);
        var totalBudgetedActual = rows.Sum(r => r.Actual);

        return new BudgetExecutionDto(
            budget.Id.Value,
            month.ToString(),
            budget.CurrencyId.Value,
            rows,
            unbudgeted,
            totalLimit,
            totalBudgetedActual,
            totalLimit - totalBudgetedActual,
            totalBudgetedActual + unbudgeted,
            RateSourceName,
            rates.Values.OfType<AppliedRateDto>().OrderBy(r => r.OperationDate).ThenBy(r => r.FromCurrencyId).ToList(),
            excluded);
    }

    /// <summary>
    /// The rate history holds pairs "currency → primary currency" (ADR-0004 §9), so a budget in a
    /// non-primary currency finds an expense in the primary one only through the inverse pair.
    /// </summary>
    private async Task<AppliedRateDto?> FindRateAsync(CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken)
    {
        var direct = await exchangeRateLookup.GetHistoricalRateAsync(from, to, date, cancellationToken);
        if (direct is { } d && d.Rate > 0)
        {
            return new AppliedRateDto(from.Value, date, d.RatesAsOfDate, d.Rate, d.RatesAsOfDate < date);
        }

        var inverse = await exchangeRateLookup.GetHistoricalRateAsync(to, from, date, cancellationToken);
        if (inverse is { } i && i.Rate > 0)
        {
            return new AppliedRateDto(from.Value, date, i.RatesAsOfDate, 1m / i.Rate, i.RatesAsOfDate < date);
        }

        return null;
    }

    private async Task<string> NoRateReasonAsync(CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken)
    {
        var fromCode = (await currencyLookup.GetAsync(from, cancellationToken))?.Code ?? from.Value.ToString();
        var toCode = (await currencyLookup.GetAsync(to, cancellationToken))?.Code ?? to.Value.ToString();
        return $"Курс валюты {fromCode} к {toCode} на дату {date:yyyy-MM-dd} недоступен.";
    }
}
