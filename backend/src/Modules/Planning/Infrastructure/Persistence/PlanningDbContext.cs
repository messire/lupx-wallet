using LupexWallet.Planning.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>Schema "planning" (schema.md) — a dedicated DbContext per module (ADR-0001).</summary>
public sealed class PlanningDbContext(DbContextOptions<PlanningDbContext> options) : DbContext(options)
{
    public const string Schema = "planning";

    public DbSet<PlannedTransaction> PlannedTransactions => Set<PlannedTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new PlannedTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new OccurrenceOverrideConfiguration());
        modelBuilder.ApplyConfiguration(new OccurrenceExecutionConfiguration());
    }
}
