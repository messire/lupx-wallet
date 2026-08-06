using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using MediatR;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Подписчики на доменные события Operations (ADR-0008) — живут в Infrastructure, а не
/// в Application, т.к. ссылаются на DomainEventNotification&lt;T&gt; (BuildingBlocks.
/// Infrastructure, MediatR-обертка) и Operations.Domain (типы событий): Application-слой
/// не должен зависеть от Infrastructure другого модуля (high-level-architecture.md, §2).
/// Регистрируются вручную в BalanceHistoryModuleExtensions — не через MediatR-сканирование
/// сборки Application (Program.cs сканирует только *.Application-сборки, см. ADR-0007/
/// список RegisterServicesFromAssemblies), а не Infrastructure.
///
/// TransferCreated/TransferDeleted не нужны отдельными подписчиками — обе "ноги" перевода
/// уже поднимают OperationCreated/OperationDeleted (см. Operations.Domain.Operation.
/// CreateForTransfer, Operations.Application.DeleteTransferCommand).
/// </summary>
public sealed class OperationCreatedHandler(BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<OperationCreated>>
{
    public Task Handle(DomainEventNotification<OperationCreated> notification, CancellationToken cancellationToken) =>
        recalculationService.RecalculateFromAsync(
            notification.DomainEvent.WalletId, notification.DomainEvent.OperationDate, cancellationToken);
}

public sealed class OperationUpdatedHandler(BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<OperationUpdated>>
{
    public async Task Handle(DomainEventNotification<OperationUpdated> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;

        if (domainEvent.PreviousWalletId == domainEvent.WalletId)
        {
            var earliestAffectedDate = domainEvent.OperationDate < domainEvent.PreviousOperationDate
                ? domainEvent.OperationDate
                : domainEvent.PreviousOperationDate;

            await recalculationService.RecalculateFromAsync(domainEvent.WalletId, earliestAffectedDate, cancellationToken);
            return;
        }

        // UC-13, решение пользователя от 2026-09-11 (перенос операции на другой кошелек):
        // каждый кошелек пересчитывается независимо (IWalletOperationsLookup фильтрует по
        // WalletId, операция теперь принадлежит только новому) — старый пересчитывается с
        // PreviousOperationDate (там, где ее вклад больше не должен учитываться), новый —
        // с OperationDate (там, где ее вклад появляется впервые).
        await recalculationService.RecalculateFromAsync(domainEvent.PreviousWalletId, domainEvent.PreviousOperationDate, cancellationToken);
        await recalculationService.RecalculateFromAsync(domainEvent.WalletId, domainEvent.OperationDate, cancellationToken);
    }
}

public sealed class OperationDeletedHandler(BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<OperationDeleted>>
{
    public Task Handle(DomainEventNotification<OperationDeleted> notification, CancellationToken cancellationToken) =>
        recalculationService.RecalculateFromAsync(
            notification.DomainEvent.WalletId, notification.DomainEvent.OperationDate, cancellationToken);
}
