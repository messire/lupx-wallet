using LupexWallet.Budgeting.Application;
using LupexWallet.Budgeting.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LupexWallet.Budgeting.Infrastructure;

/// <summary>The pre-checks in the handlers give the readable message; the unique indexes catch the concurrent case.</summary>
public sealed class BudgetingUnitOfWork(BudgetingDbContext dbContext) : IBudgetingUnitOfWork
{
    private const string UniqueViolationSqlState = "23505";

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState } pg)
        {
            throw pg.ConstraintName switch
            {
                ExpenseCategoryConfiguration.OperationTypeUniqueIndex => new OperationTypeClaimedConcurrentlyException(),
                ExpenseCategoryConfiguration.ActiveNameUniqueIndex => new ExpenseCategoryNameAlreadyExistsException(
                    ex.Entries.Select(e => e.Entity).OfType<ExpenseCategory>().FirstOrDefault()?.Name ?? string.Empty),
                BudgetConfiguration.MonthCurrencyUniqueIndex => new BudgetingConflictException(
                    "Бюджет на этот месяц в этой валюте уже есть (создан параллельно)."),
                _ => ex,
            };
        }
    }
}
