using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>A stated balance to be absorbed by a correction on <see cref="Date"/>.</summary>
public sealed record CorrectionInput(OperationId Id, DateOnly Date, decimal StatedBalance);

/// <summary>The calculated signed amount of the correction with the given id.</summary>
public sealed record CorrectionAmount(OperationId Id, decimal Amount);

/// <summary>
/// Pure calculation of balance corrections (ADR-0003, §6). For every stated balance X on day D,
/// in ascending date order, the correction is X minus the balance at the end of D without it:
/// initial balance + regular operations dated up to and including D + the corrections of the
/// earlier dates. So each stated balance absorbs only the discrepancy left after the previous
/// one and never affects the balance before its own date.
/// </summary>
public static class StatedBalanceCorrectionCalculator
{
    public static IReadOnlyList<CorrectionAmount> Calculate(
        decimal initialBalance,
        IEnumerable<WalletLedgerEntry> regularOperations,
        IEnumerable<CorrectionInput> corrections)
    {
        var ledger = regularOperations.OrderBy(x => x.OperationDate).ToList();
        var ordered = corrections.OrderBy(x => x.Date).ToList();

        var result = new List<CorrectionAmount>(ordered.Count);
        var regularSum = 0m;
        var correctionsSum = 0m;
        var next = 0;

        foreach (var correction in ordered)
        {
            while (next < ledger.Count && ledger[next].OperationDate <= correction.Date)
            {
                regularSum += ledger[next].AppliedDelta;
                next++;
            }

            var amount = correction.StatedBalance - (initialBalance + regularSum + correctionsSum);
            result.Add(new CorrectionAmount(correction.Id, amount));
            correctionsSum += amount;
        }

        return result;
    }
}
