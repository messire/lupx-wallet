using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class WalletOperationsLookup(OperationsDbContext dbContext) : IWalletOperationsLookup
{
    public async Task<IReadOnlyList<DailyOperationsDelta>> GetDailyDeltasUpToAsync(
        WalletId walletId, DateOnly throughDate, CancellationToken cancellationToken)
    {
        // Группировка/суммирование по дате выполняется в памяти (не через LINQ GroupBy+Sum
        // в БД) — EF.Property на приватном backing field (_appliedDeltaAmount) надежно
        // транслируется в простой SELECT-проекции, но не гарантированно — в комбинации с
        // GroupBy/Sum на стороне Postgres/Npgsql. Объём операций одного кошелька в этом
        // приложении (личные финансы одного пользователя) не оправдывает риск.
        // Сортировка не нужна в самом SQL-запросе — строки все равно перегруппировываются
        // и пересортировываются по датам в памяти ниже.
        var rows = await dbContext.Operations
            .Where(x => x.WalletId == walletId && x.OperationDate <= throughDate)
            .Select(x => new { x.OperationDate, Amount = EF.Property<decimal>(x, "_appliedDeltaAmount") })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.OperationDate)
            .Select(g => new DailyOperationsDelta(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.OperationDate)
            .ToList();
    }
}
