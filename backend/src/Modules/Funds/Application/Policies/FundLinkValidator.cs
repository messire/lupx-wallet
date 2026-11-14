using LupexWallet.Debts.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Application;

/// <summary>
/// Checks the optional debt/loan link of a fund through the Debts-owned read port (ADR-0010 §4):
/// the debt/loan must exist, be in the fund currency and, for a new link, not be cancelled.
/// </summary>
public sealed class FundLinkValidator(IDebtObligationsLookup obligations)
{
    /// <param name="isNewLink">False when the link is unchanged: an already linked debt may have been cancelled since.</param>
    public async Task EnsureValidAsync(
        Guid? linkedDebtId, Guid? linkedLoanId, CurrencyId fundCurrencyId, bool isNewLink, CancellationToken cancellationToken)
    {
        ObligationReference? obligation;
        if (linkedDebtId is { } debtId)
        {
            obligation = await obligations.FindDebtAsync(debtId, cancellationToken) ?? throw new FundLinkedDebtNotFoundException();
        }
        else if (linkedLoanId is { } loanId)
        {
            obligation = await obligations.FindLoanAsync(loanId, cancellationToken) ?? throw new FundLinkedLoanNotFoundException();
        }
        else
        {
            return;
        }

        if (obligation.CurrencyId != fundCurrencyId)
        {
            throw new FundLinkCurrencyMismatchException();
        }

        if (isNewLink && obligation.IsCancelled)
        {
            throw new FundLinkedObligationCancelledException();
        }
    }
}
