using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Writing port published by Operations for modules that record a real operation as part of
/// their own command (ADR-0010: debt/loan repayment, execution of a planned transaction).
/// Runs the regular CreateOperation use case inside the caller's transaction, so balance,
/// balance history and audit behave exactly as for an operation created through the API.
/// </summary>
public interface IOperationRecorder
{
    Task<RecordedOperation> RecordAsync(
        WalletId walletId, OperationTypeId operationTypeId, decimal amount, DateOnly operationDate,
        CancellationToken cancellationToken);
}

public sealed record RecordedOperation(
    OperationId OperationId, WalletId WalletId, CurrencyId CurrencyId, decimal Amount, DateOnly OperationDate);
