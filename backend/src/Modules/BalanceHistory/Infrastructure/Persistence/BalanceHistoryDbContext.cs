using LupexWallet.BalanceHistory.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>Schema "balance_history" (schema.md) — a separate DbContext per module (ADR-0006).</summary>
public sealed class BalanceHistoryDbContext(DbContextOptions<BalanceHistoryDbContext> options) : DbContext(options)
{
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("balance_history");
        modelBuilder.ApplyConfiguration(new BalanceSnapshotConfiguration());
    }
}
