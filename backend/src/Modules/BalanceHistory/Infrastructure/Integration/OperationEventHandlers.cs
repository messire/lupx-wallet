using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using MediatR;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Subscribers to Operations domain events (ADR-0001) — live in Infrastructure, not
/// Application, because they reference DomainEventNotification&lt;T&gt; (BuildingBlocks.
/// Infrastructure, a MediatR wrapper) and Operations.Domain (event types): the Application
/// layer must not depend on another module's Infrastructure (high-level-architecture.md, §2).
/// Registered manually in BalanceHistoryModuleExtensions — not via MediatR scanning of the
/// Application assembly (Program.cs only scans *.Application assemblies, see the
/// RegisterServicesFromAssemblies list in ADR-0001), not Infrastructure.
///
/// TransferCreated/TransferDeleted/TransferRestored don't need separate subscribers — both
/// "legs" of a transfer already raise OperationCreated/OperationDeleted/OperationRestored,
/// so both wallets are recalculated through the leg events (ADR-0002, §3).
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

/// <summary>Restoring a soft-deleted operation brings its effect back (ADR-0002, §3) — same cascade as deletion, from the operation date (ADR-0003, §2).</summary>
public sealed class OperationRestoredHandler(BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<OperationRestored>>
{
    public Task Handle(DomainEventNotification<OperationRestored> notification, CancellationToken cancellationToken) =>
        recalculationService.RecalculateFromAsync(
            notification.DomainEvent.WalletId, notification.DomainEvent.OperationDate, cancellationToken);
}

/// <summary>
/// The system changed the amount of a balance correction (ADR-0003, §6). The correction is an
/// ordinary operation row with its own applied delta, so the cascade is the same as for an edit,
/// from the correction's date.
/// </summary>
public sealed class CorrectionRecalculatedHandler(BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<CorrectionRecalculated>>
{
    public Task Handle(DomainEventNotification<CorrectionRecalculated> notification, CancellationToken cancellationToken) =>
        recalculationService.RecalculateFromAsync(
            notification.DomainEvent.WalletId, notification.DomainEvent.OperationDate, cancellationToken);
}
