using LupexWallet.Debts.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Debts.Infrastructure;

/// <summary>Schema "debts" (schema.md) — one DbContext per module (ADR-0001).</summary>
public sealed class DebtsDbContext(DbContextOptions<DebtsDbContext> options) : DbContext(options)
{
    public DbSet<Creditor> Creditors => Set<Creditor>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("debts");
        modelBuilder.ApplyConfiguration(new CreditorConfiguration());
        modelBuilder.ApplyConfiguration(new DebtConfiguration());
        modelBuilder.ApplyConfiguration(new DebtRepaymentConfiguration());
        modelBuilder.ApplyConfiguration(new LoanConfiguration());
        modelBuilder.ApplyConfiguration(new LoanScheduleItemConfiguration());
        modelBuilder.ApplyConfiguration(new LoanPaymentConfiguration());
    }
}
