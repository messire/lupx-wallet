using LupexWallet.Funds.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>Schema "funds" (schema.md) — a dedicated DbContext per module (ADR-0001).</summary>
public sealed class FundsDbContext(DbContextOptions<FundsDbContext> options) : DbContext(options)
{
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<FundAllocation> FundAllocations => Set<FundAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("funds");
        modelBuilder.ApplyConfiguration(new FundConfiguration());
        modelBuilder.ApplyConfiguration(new FundAllocationConfiguration());
    }
}
