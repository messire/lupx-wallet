using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>
/// UC-24 (docs/api/openapi.yaml: PUT /wallets/{walletId}/stated-balances/{date}) — the user states
/// the wallet balance at the end of a day; the discrepancy with the operations is recorded as a
/// balance correction (ADR-0003, §6).
/// </summary>
public sealed record SetStatedBalanceCommand(Guid WalletId, DateOnly Date, decimal Amount)
    : IRequest<SetStatedBalanceResult>, ICommand<SetStatedBalanceResult>;

/// <summary>The correction row of the date; <see cref="Created"/> is false when an earlier statement was replaced.</summary>
public sealed record SetStatedBalanceResult(OperationDto Operation, bool Created);

public sealed class SetStatedBalanceCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<SetStatedBalanceCommand, SetStatedBalanceResult>
{
    public async Task<SetStatedBalanceResult> Handle(SetStatedBalanceCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(request.WalletId);

        OperationDateValidator.EnsureInRange(request.Date, wallet.AccountingStartDate);

        var statedBalance = new Money(request.Amount, wallet.CurrencyId);
        var correction = await repository.GetCorrectionAsync(walletId, request.Date, cancellationToken);
        var created = correction is null;

        if (correction is null)
        {
            correction = Operation.CreateCorrection(OperationId.New(), walletId, wallet.CurrencyId, request.Date, statedBalance.Amount);
            repository.Add(correction);
        }
        else
        {
            correction.ReplaceStatedBalance(statedBalance);
        }

        // Recalculates every correction of the wallet (this one included) and yields the net balance change.
        var correctionsChange = await correctionsReconciler.ReconcileAsync(walletId, [correction], cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, walletId, new Money(correctionsChange, wallet.CurrencyId), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetStatedBalanceResult(OperationDto.FromDomain(correction), created);
    }
}
