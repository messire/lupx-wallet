using System.Transactions;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Wraps command handling (ICommand only, not queries) in a TransactionScope so that SaveChanges
/// calls of several DbContexts (the initiating module plus event subscribers) commit as one
/// PostgreSQL transaction (ADR-0001, §4). Constrained by ICommand&lt;TResponse&gt; rather than
/// IRequest&lt;TResponse&gt;: the void IRequest is not IRequest&lt;Unit&gt; in MediatR 12, so the latter
/// would make DI silently skip the behavior for such commands.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull, ICommand<TResponse>
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
