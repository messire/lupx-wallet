using System.Transactions;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Оборачивает обработку MediatR-команды в System.Transactions.TransactionScope, чтобы
/// SaveChanges нескольких DbContext (модуль-инициатор + подписчики его доменных событий,
/// например Wallets/BalanceHistory/Audit) фиксировались как одна физическая транзакция
/// PostgreSQL — сильная согласованность вместо eventual consistency
/// (high-level-architecture.md, §4; ADR-0006).
///
/// Применяется только к командам (ICommand), не к запросам — чтения не должны
/// открывать транзакцию.
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
