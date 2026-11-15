using LupexWallet.Debts.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Application;

/// <summary>Reference checks for creating/changing a debt or a loan (ADR-0002 §4: a deactivated item is not used in new references).</summary>
public sealed class DebtsReferenceValidator(ICreditorRepository creditors, ICurrencyLookup currencyLookup)
{
    /// <summary>The creditor must exist; it must be active unless it is the unchanged current one.</summary>
    public async Task EnsureCreditorAsync(CreditorId creditorId, CreditorId? currentCreditorId, CancellationToken cancellationToken)
    {
        var creditor = await creditors.GetByIdAsync(creditorId, cancellationToken)
            ?? throw new CreditorReferenceNotFoundException();
        if (!creditor.IsActive && creditorId != currentCreditorId)
        {
            throw new CreditorReferenceInactiveException();
        }
    }

    public async Task EnsureCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken)
    {
        _ = await currencyLookup.GetAsync(currencyId, cancellationToken)
            ?? throw new DebtsCurrencyReferenceNotFoundException();
    }
}
