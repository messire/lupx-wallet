using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class ReferenceDataUnitOfWork(ReferenceDataDbContext dbContext) : IReferenceDataUnitOfWork
{
    private const string UniqueViolationSqlState = "23505";
    private const string CurrencyCodeIndexName = "ux_currencies_code";

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState, ConstraintName: CurrencyCodeIndexName })
        {
            // Concurrent activation of the same code: the pre-check passed for both requests.
            var code = ex.Entries.Select(e => e.Entity).OfType<Currency>().FirstOrDefault()?.Code ?? string.Empty;
            throw new CurrencyCodeAlreadyExistsException(code);
        }
    }

    public async Task LockCurrencyCodeAsync(string code, CancellationToken cancellationToken)
    {
        var lockKey = LockKey(code);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
    }

    public async Task LockCurrencyCodeSharedAsync(string code, CancellationToken cancellationToken)
    {
        var lockKey = LockKey(code);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock_shared(hashtext({lockKey}))", cancellationToken);
    }

    private static string LockKey(string code) => "currency:" + code.Trim().ToUpperInvariant();
}
