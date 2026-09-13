using System.Transactions;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Wraps MediatR command handling in a System.Transactions.TransactionScope so that
/// SaveChanges calls from multiple DbContexts (the initiating module plus subscribers to
/// its domain events, e.g. Wallets/BalanceHistory/Audit) commit as a single physical
/// PostgreSQL transaction — strong consistency instead of eventual consistency
/// (high-level-architecture.md, §4; ADR-0006).
///
/// Applies only to commands (ICommand), not queries — reads must not open a transaction.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>, ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogDebug("Начало транзакции для команды {RequestName}", requestName);

        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        var response = await next();

        scope.Complete();
        logger.LogDebug("Транзакция для команды {RequestName} зафиксирована", requestName);

        return response;
    }
}
