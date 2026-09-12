using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-16 (docs/api/openapi.yaml: POST /transfers).</summary>
public sealed record CreateTransferCommand(
    Guid SourceWalletId,
    Guid TargetWalletId,
    decimal Amount,
    DateOnly TransferDate) : IRequest<TransferDto>, ICommand<TransferDto>;

public sealed class CreateTransferCommandHandler(
    IOperationRepository operationRepository,
    ITransferRepository transferRepository,
    IOperationsUnitOfWork unitOfWork,
    IOperationTypeLookup operationTypeLookup,
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<CreateTransferCommand, TransferDto>
{
    public async Task<TransferDto> Handle(CreateTransferCommand request, CancellationToken cancellationToken)
    {
        var sourceWalletId = new WalletId(request.SourceWalletId);
        var targetWalletId = new WalletId(request.TargetWalletId);

        var sourceWallet = await walletBalanceGateway.GetAsync(sourceWalletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(request.SourceWalletId);
        var targetWallet = await walletBalanceGateway.GetAsync(targetWalletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(request.TargetWalletId);

        if (sourceWallet.CurrencyId != targetWallet.CurrencyId)
        {
            throw new TransferCurrencyMismatchException();
        }

        // Дата перевода должна быть допустимой для ОБОИХ кошельков — берем более позднюю
        // (более строгую) из двух AccountingStartDate как нижнюю границу.
        var effectiveAccountingStartDate = sourceWallet.AccountingStartDate > targetWallet.AccountingStartDate
            ? sourceWallet.AccountingStartDate
            : targetWallet.AccountingStartDate;
        OperationDateValidator.EnsureInRange(request.TransferDate, effectiveAccountingStartDate);

        var operationType = await operationTypeLookup.FindActiveByBehaviorKindCodeAsync("Transfer", cancellationToken)
            ?? throw new NoActiveOperationTypeForTransferException();

        var amount = new Money(request.Amount, sourceWallet.CurrencyId);

        var (transfer, sourceOperation, targetOperation) = Transfer.Create(
            TransferId.New(), sourceWalletId, targetWalletId, operationType.Id, amount, request.TransferDate);

        await walletBalanceGateway.ApplyDeltaAsync(sourceWalletId, sourceOperation.AppliedDelta, cancellationToken);
        await walletBalanceGateway.ApplyDeltaAsync(targetWalletId, targetOperation.AppliedDelta, cancellationToken);

        operationRepository.Add(sourceOperation);
        operationRepository.Add(targetOperation);
        transferRepository.Add(transfer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TransferDto.FromDomain(transfer);
    }
}
