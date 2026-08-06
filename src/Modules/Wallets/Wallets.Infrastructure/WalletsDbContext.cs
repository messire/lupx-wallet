using LupexWallet.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>Схема "wallets" (schema.md) — отдельный DbContext на модуль (ADR-0006).</summary>
public sealed class WalletsDbContext(DbContextOptions<WalletsDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("wallets");
        modelBuilder.ApplyConfiguration(new WalletConfiguration());
    }
}
