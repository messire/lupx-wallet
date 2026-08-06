using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// ADR-0009, случай a — одна из двух реализаций Wallets.Application.IWalletHistorySource:
/// владелец данных Operations. true, если у кошелька есть хотя бы одна Operation (включая
/// обе "ноги" переводов — они хранятся как обычные Operation, см. Transfer.Create).
/// Зарегистрирована в AddOperationsModule.
/// </summary>
public sealed class OperationsWalletHistorySource(OperationsDbContext dbContext) : IWalletHistorySource
{
    public Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.Operations.AnyAsync(x => x.WalletId == walletId, cancellationToken);
}
