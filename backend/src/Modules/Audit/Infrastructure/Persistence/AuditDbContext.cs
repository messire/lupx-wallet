using LupexWallet.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>Schema "audit" (schema.md) — a separate DbContext per module (ADR-0006).</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
    }
}
