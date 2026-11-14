using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationRecorder(ISender sender) : IOperationRecorder
{
    public async Task<RecordedOperation> RecordAsync(
        WalletId walletId, OperationTypeId operationTypeId, decimal amount, DateOnly operationDate,
        CancellationToken cancellationToken)
    {
        // The nested command joins the caller's ambient TransactionScope (TransactionBehavior uses Required).
        var dto = await sender.Send(
            new CreateOperationCommand(walletId.Value, operationTypeId.Value, amount, operationDate, AdjustmentMode: null),
            cancellationToken);

        return new RecordedOperation(
            new OperationId(dto.Id), new WalletId(dto.WalletId), new CurrencyId(dto.CurrencyId), dto.Amount, dto.OperationDate);
    }
}
