using LupexWallet.Operations.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>Schema "operations" (schema.md) — one DbContext per module (ADR-0006).</summary>
public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options) : DbContext(options)
{
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("operations");
        modelBuilder.ApplyConfiguration(new OperationConfiguration());
        modelBuilder.ApplyConfiguration(new TransferConfiguration());
    }
}
