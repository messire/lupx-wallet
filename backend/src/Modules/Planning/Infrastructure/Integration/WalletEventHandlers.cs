using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Planning.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>
/// Subscribers to Wallets events (ADR-0001 §2 way e): a plan's default wallet must exist and be in the
/// plan currency, so a deleted wallet, or one that changed to another currency, is unlinked from its
/// plans (audited as PlannedTransaction/Updated). Archiving keeps the link. Registered in AddPlanningModule.
/// </summary>
public sealed class PlanningWalletDeletedHandler(IPlannedTransactionRepository repository, IPlanningUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<WalletDeleted>>
{
    public async Task Handle(DomainEventNotification<WalletDeleted> notification, CancellationToken cancellationToken)
    {
        var plans = await repository.ListByWalletAsync(notification.DomainEvent.WalletId, cancellationToken);
        if (plans.Count == 0)
        {
            return;
        }

        foreach (var plan in plans)
        {
            plan.UnlinkWallet();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

public sealed class PlanningWalletCurrencyChangedHandler(IPlannedTransactionRepository repository, IPlanningUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>
{
    public async Task Handle(DomainEventNotification<WalletCurrencyChanged> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        var plans = (await repository.ListByWalletAsync(domainEvent.WalletId, cancellationToken))
            .Where(p => p.CurrencyId != domainEvent.NewCurrencyId)
            .ToList();
        if (plans.Count == 0)
        {
            return;
        }

        foreach (var plan in plans)
        {
            plan.UnlinkWallet();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
