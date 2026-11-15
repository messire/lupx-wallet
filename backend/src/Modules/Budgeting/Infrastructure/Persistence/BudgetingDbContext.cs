using LupexWallet.Budgeting.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Budgeting.Infrastructure;

/// <summary>Schema "budgeting" (schema.md) — a separate DbContext per module (ADR-0001).</summary>
public sealed class BudgetingDbContext(DbContextOptions<BudgetingDbContext> options) : DbContext(options)
{
    public const string Schema = "budgeting";

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new ExpenseCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new BudgetConfiguration());
    }
}
