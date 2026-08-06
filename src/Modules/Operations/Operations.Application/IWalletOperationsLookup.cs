using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем Operations для модуля BalanceHistory
/// (ADR-0008, по аналогии с ADR-0007) — агрегированные по дате знаковые дельты операций
/// кошелька (включая обе "ноги" переводов — они хранятся как обычные Operation с уже
/// проставленным AppliedDelta, см. Transfer.Create), нужные для восстановления баланса
/// на произвольную дату без повторного вычисления knowledge о поведении типов операций
/// (это уже сделано один раз при создании/изменении Operation — AppliedDelta).
/// </summary>
public interface IWalletOperationsLookup
{
    /// <summary>
    /// Дельты, сгруппированные по OperationDate, по возрастанию даты, для всех операций
    /// кошелька с OperationDate меньше либо равной <paramref name="throughDate"/>.
    /// </summary>
    Task<IReadOnlyList<DailyOperationsDelta>> GetDailyDeltasUpToAsync(
        WalletId walletId, DateOnly throughDate, CancellationToken cancellationToken);
}

public sealed record DailyOperationsDelta(DateOnly OperationDate, decimal DeltaAmount);
