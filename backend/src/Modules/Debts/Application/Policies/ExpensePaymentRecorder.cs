using LupexWallet.Debts.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Debts.Application;

/// <summary>
/// Records a debt repayment or a loan payment as a regular expense operation (ADR-0010 §3, B4)
/// through <see cref="IOperationRecorder"/>, inside the caller's transaction. Checks what
/// Operations would reject, up front, so the client gets a Debts error instead of an
/// Operations one.
/// </summary>
public sealed class ExpensePaymentRecorder(
    IOperationTypeLookup operationTypeLookup,
    IWalletBalanceGateway walletBalanceGateway,
    IOperationRecorder operationRecorder)
{
    /// <summary>Returns the wallet currency; the caller compares it with the obligation currency.</summary>
    public async Task<CurrencyId> ValidateAsync(
        WalletId walletId, OperationTypeId operationTypeId, DateOnly paymentDate, CancellationToken cancellationToken)
    {
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new PaymentOperationTypeNotFoundException();
        if (!operationType.IsActive)
        {
            throw new PaymentOperationTypeInactiveException();
        }

        if (operationType.BehaviorKindCode != "Expense")
        {
            throw new PaymentOperationTypeNotExpenseException();
        }

        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new PaymentWalletNotFoundException();

        // Same bounds as Operations' OperationDateValidator.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (paymentDate < wallet.AccountingStartDate || paymentDate > today)
        {
            throw new PaymentDateOutOfRangeException(paymentDate, wallet.AccountingStartDate, today);
        }

        return wallet.CurrencyId;
    }

    public Task<RecordedOperation> RecordAsync(
        WalletId walletId, OperationTypeId operationTypeId, decimal amount, DateOnly paymentDate, CancellationToken cancellationToken) =>
        operationRecorder.RecordAsync(walletId, operationTypeId, amount, paymentDate, cancellationToken);
}
