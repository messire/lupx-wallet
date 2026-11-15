using LupexWallet.Planning.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Planning.Application;

/// <summary>
/// Validates the references of a plan through other modules' ports (ADR-0001 §2): operation type
/// (exists, Income/Expense, active unless unchanged), active currency (unless unchanged) and the
/// optional wallet in the plan currency.
/// </summary>
public sealed class PlannedTransactionReferences(
    IOperationTypeLookup operationTypeLookup,
    ICurrencyLookup currencyLookup,
    IWalletBalanceGateway walletBalanceGateway)
{
    public async Task<PlannedDirection> ResolveDirectionAsync(
        OperationTypeId operationTypeId, OperationTypeId? currentOperationTypeId, CancellationToken cancellationToken)
    {
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new PlannedOperationTypeNotFoundException();

        if (!operationType.IsActive && operationTypeId != currentOperationTypeId)
        {
            throw new PlannedOperationTypeInactiveException();
        }

        return ParseDirection(operationType.BehaviorKindCode);
    }

    public async Task EnsureCurrencyAsync(CurrencyId currencyId, CurrencyId? currentCurrencyId, CancellationToken cancellationToken)
    {
        if (currencyId == currentCurrencyId)
        {
            return;
        }

        _ = await currencyLookup.GetAsync(currencyId, cancellationToken) ?? throw new PlannedCurrencyNotFoundException();
    }

    public async Task<WalletBalanceInfo> GetWalletInCurrencyAsync(WalletId walletId, CurrencyId currencyId, CancellationToken cancellationToken)
    {
        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new PlannedWalletNotFoundException();

        if (wallet.CurrencyId != currencyId)
        {
            throw new PlannedWalletCurrencyMismatchException();
        }

        return wallet;
    }

    public static PlannedDirection ParseDirection(string behaviorKindCode) => behaviorKindCode switch
    {
        "Income" => PlannedDirection.Income,
        "Expense" => PlannedDirection.Expense,
        _ => throw new PlannedOperationTypeBehaviorNotSupportedException(),
    };
}
