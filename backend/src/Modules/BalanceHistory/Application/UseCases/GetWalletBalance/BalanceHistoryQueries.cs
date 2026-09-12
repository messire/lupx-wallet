using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>UC-18/UC-20 (docs/api/openapi.yaml: GET /wallets/{walletId}/balance). Null → wallet not found (404).</summary>
public sealed record GetWalletBalanceQuery(Guid WalletId, DateOnly? Date) : IRequest<BalanceSnapshotDto?>, IQuery<BalanceSnapshotDto?>;

public sealed class GetWalletBalanceQueryHandler(
    IBalanceSnapshotRepository repository,
    IWalletBalanceGateway walletGateway,
    IWalletOperationsLookup operationsLookup) : IRequestHandler<GetWalletBalanceQuery, BalanceSnapshotDto?>
{
    public async Task<BalanceSnapshotDto?> Handle(GetWalletBalanceQuery request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null)
        {
            return null;
        }

        if (request.Date is not { } date)
        {
            // Без параметра date — текущий баланс (docs/api/openapi.yaml), поддерживается
            // синхронно на каждой операции через IWalletBalanceGateway (ADR-0007), поэтому
            // всегда актуален без обращения к материализованным слепкам.
            return new BalanceSnapshotDto(
                wallet.Id.Value, DateOnly.FromDateTime(DateTime.UtcNow), wallet.CurrentBalance.Amount, wallet.CurrencyId.Value);
        }

        if (date < wallet.AccountingStartDate)
        {
            // Решено, Q13: баланс раньше accounting_start_date всегда равен нулю, слепок не материализуется.
            return new BalanceSnapshotDto(wallet.Id.Value, date, 0m, wallet.CurrencyId.Value);
        }

        var snapshot = await repository.GetAsync(walletId, date, cancellationToken);
        if (snapshot is not null)
        {
            return BalanceSnapshotDto.FromDomain(snapshot);
        }

        // Слепок на дату еще не материализован (например, запрошена сегодняшняя дата до
        // первого прогона плановой/досоздающей задачи, ADR-0004) — считаем по тому же
        // алгоритму на лету, не сохраняя результат; материализация — забота
        // BalanceRecalculationService и фоновой задачи, а не read-пути.
        var deltas = await operationsLookup.GetDailyDeltasUpToAsync(walletId, date, cancellationToken);
        var balanceAmount = wallet.InitialBalance.Amount + deltas.Sum(d => d.DeltaAmount);
        return new BalanceSnapshotDto(wallet.Id.Value, date, balanceAmount, wallet.CurrencyId.Value);
    }
}

/// <summary>Wallet balance snapshot history for a period (docs/api/openapi.yaml: GET /wallets/{walletId}/balance-history). Null → wallet not found (404).</summary>
public sealed record ListWalletBalanceHistoryQuery(
    Guid WalletId, DateOnly From, DateOnly To, string? Cursor, int Limit) : IRequest<BalanceSnapshotPageDto?>, IQuery<BalanceSnapshotPageDto?>;

public sealed class ListWalletBalanceHistoryQueryHandler(
    IBalanceSnapshotRepository repository,
    IWalletBalanceGateway walletGateway) : IRequestHandler<ListWalletBalanceHistoryQuery, BalanceSnapshotPageDto?>
{
    public async Task<BalanceSnapshotPageDto?> Handle(ListWalletBalanceHistoryQuery request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null)
        {
            return null;
        }

        var cursor = BalanceSnapshotCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(walletId, request.From, request.To, cursor, limit, cancellationToken);

        var items = page.Items.Select(BalanceSnapshotDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new BalanceSnapshotCursor(page.Items[^1].SnapshotDate).Encode()
            : null;

        return new BalanceSnapshotPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
