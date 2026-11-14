using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Subscribers to Wallets domain events (ADR-0002, §2) — in Infrastructure for the same reason as
/// the BalanceHistory ones (DomainEventNotification + another module's Domain, ADR-0001, §2 way e);
/// registered manually in OperationsModuleExtensions. A balance correction is not wallet history
/// (ADR-0002, §1): it is removed together with the wallet and follows the wallet currency.
/// Both handlers take the wallet lock, so they serialize with the commands changing its operations.
/// </summary>
public sealed class WalletDeletedHandler(IOperationRepository repository, IOperationsUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<WalletDeleted>>
{
    public async Task Handle(DomainEventNotification<WalletDeleted> notification, CancellationToken cancellationToken)
    {
        var walletId = notification.DomainEvent.WalletId;
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var corrections = await repository.GetCorrectionsAsync(walletId, cancellationToken);
        if (corrections.Count == 0)
        {
            return;
        }

        foreach (var correction in corrections)
        {
            correction.MarkRemovedWithWallet();
            repository.RemoveCorrection(correction);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// ADR-0002, §2: a wallet without operations may change currency even if it has stated balances.
/// Their numbers do not change; only the currency of the corrections follows the wallet.
/// </summary>
public sealed class WalletCurrencyChangedHandler(IOperationRepository repository, IOperationsUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>
{
    public async Task Handle(DomainEventNotification<WalletCurrencyChanged> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        await unitOfWork.AcquireWalletLocksAsync([domainEvent.WalletId], cancellationToken);

        var corrections = await repository.GetCorrectionsAsync(domainEvent.WalletId, cancellationToken);
        if (corrections.Count == 0)
        {
            return;
        }

        foreach (var correction in corrections)
        {
            correction.RebaseCurrency(domainEvent.NewCurrencyId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
