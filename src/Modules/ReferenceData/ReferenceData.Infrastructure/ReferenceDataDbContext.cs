using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>Схема "reference_data" (schema.md) — отдельный DbContext на модуль (ADR-0006).</summary>
public sealed class ReferenceDataDbContext(DbContextOptions<ReferenceDataDbContext> options) : DbContext(options)
{
    public DbSet<WalletType> WalletTypes => Set<WalletType>();
    public DbSet<OperationType> OperationTypes => Set<OperationType>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<OperationBehaviorKind> OperationBehaviorKinds => Set<OperationBehaviorKind>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reference_data");
        modelBuilder.ApplyConfiguration(new WalletTypeConfiguration());
        modelBuilder.ApplyConfiguration(new OperationTypeConfiguration());
        modelBuilder.ApplyConfiguration(new CurrencyConfiguration());
        modelBuilder.ApplyConfiguration(new OperationBehaviorKindConfiguration());
    }
}
