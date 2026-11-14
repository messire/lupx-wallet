using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// StatedBalanceCorrectionCalculator (ADR-0003, §6): the correction on day D is the stated balance
/// minus the end-of-day balance without it. The example of the ADR is replayed step by step; the
/// invariant "balance at the end of D equals the stated balance" is checked independently of the
/// calculator's own formula.
/// </summary>
public sealed class StatedBalanceCorrectionCalculatorTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep15 = new(2026, 9, 15);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);

    private static WalletLedgerEntry Entry(DateOnly date, decimal delta) => new(date, delta);

    private static (CorrectionInput Input, OperationId Id) Stated(DateOnly date, decimal balance)
    {
        var id = OperationId.New();
        return (new CorrectionInput(id, date, balance), id);
    }

    private static decimal AmountOf(IReadOnlyList<CorrectionAmount> result, OperationId id) =>
        result.Single(x => x.Id == id).Amount;

    /// <summary>Balance at the end of <paramref name="date"/>: initial + regular + corrections up to the date.</summary>
    private static decimal BalanceAtEndOf(
        DateOnly date, decimal initial, IEnumerable<WalletLedgerEntry> ledger,
        IEnumerable<CorrectionInput> corrections, IReadOnlyList<CorrectionAmount> amounts) =>
        initial
        + ledger.Where(x => x.OperationDate <= date).Sum(x => x.AppliedDelta)
        + corrections.Where(x => x.Date <= date).Sum(x => AmountOf(amounts, x.Id));

    private static void AssertEveryStatedBalanceHolds(
        decimal initial, IReadOnlyList<WalletLedgerEntry> ledger, IReadOnlyList<CorrectionInput> corrections, IReadOnlyList<CorrectionAmount> amounts)
    {
        foreach (var correction in corrections)
        {
            BalanceAtEndOf(correction.Date, initial, ledger, corrections, amounts)
                .Should().Be(correction.StatedBalance, $"the balance at the end of {correction.Date} must equal the stated one");
        }
    }

    // ---- The example of ADR-0003, §6 ----

    [Fact]
    public void AdrExample_Step1_ReportsIncomeOf50OnThe10thAndExpenseOf150OnThe20th()
    {
        var ledger = new[] { Entry(Sep1, 500m), Entry(Sep10, -200m), Entry(Sep20, 400m) };
        var (tenth, tenthId) = Stated(Sep10, 350m);
        var (twentieth, twentiethId) = Stated(Sep20, 600m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [tenth, twentieth]);

        AmountOf(result, tenthId).Should().Be(50m, "500 - 200 = 300 by the operations, 350 stated: unexplained income");
        AmountOf(result, twentiethId).Should().Be(-150m, "300 + 50 + 400 = 750 by the records, 600 stated: unexplained expense");
        AssertEveryStatedBalanceHolds(0m, ledger, [tenth, twentieth], result);
    }

    [Fact]
    public void AdrExample_Step2_ExpenseOfMinus100OnThe15thTurnsTheCorrectionOfThe20thIntoMinus50AndLeavesThe10thAlone()
    {
        var ledger = new[] { Entry(Sep1, 500m), Entry(Sep10, -200m), Entry(Sep15, -100m), Entry(Sep20, 400m) };
        var (tenth, tenthId) = Stated(Sep10, 350m);
        var (twentieth, twentiethId) = Stated(Sep20, 600m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [tenth, twentieth]);

        AmountOf(result, tenthId).Should().Be(50m, "an operation dated after the 10th does not change the 10th");
        AmountOf(result, twentiethId).Should().Be(-50m);
        AssertEveryStatedBalanceHolds(0m, ledger, [tenth, twentieth], result);
    }

    [Fact]
    public void AdrExample_Step3_EditingTheFirstIncomeTo600TurnsTheCorrectionOfThe10thIntoMinus50AndKeepsLaterBalances()
    {
        var ledger = new[] { Entry(Sep1, 600m), Entry(Sep10, -200m), Entry(Sep15, -100m), Entry(Sep20, 400m) };
        var (tenth, tenthId) = Stated(Sep10, 350m);
        var (twentieth, twentiethId) = Stated(Sep20, 600m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [tenth, twentieth]);

        AmountOf(result, tenthId).Should().Be(-50m, "the sign flipped: unexplained expense instead of income");
        AmountOf(result, twentiethId).Should().Be(-50m, "the 20th does not change: the 10th absorbs the edit");
        AssertEveryStatedBalanceHolds(0m, ledger, [tenth, twentieth], result);
    }

    // ---- Rules of §6 ----

    [Fact]
    public void OperationsAfterTheStatedDate_DoNotChangeItsCorrection()
    {
        var (stated, id) = Stated(Sep10, 350m);
        var before = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 500m), Entry(Sep10, -200m)], [stated]);

        var after = StatedBalanceCorrectionCalculator.Calculate(
            0m, [Entry(Sep1, 500m), Entry(Sep10, -200m), Entry(Sep15, 9_999m), Entry(Sep20, -123m)], [stated]);

        AmountOf(after, id).Should().Be(AmountOf(before, id));
    }

    [Fact]
    public void EndOfDay_OperationDatedOnTheStatedDateIsAbsorbedRegardlessOfWhenItWasEntered()
    {
        var (stated, id) = Stated(Sep10, 350m);
        var ledger = new List<WalletLedgerEntry> { Entry(Sep1, 500m), Entry(Sep10, -200m) };
        var statedFirst = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [stated]);
        AmountOf(statedFirst, id).Should().Be(50m);

        // Another -30 dated the 10th is entered after the statement: the day's end is still 350.
        ledger.Add(Entry(Sep10, -30m));
        var afterLateEntry = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [stated]);

        AmountOf(afterLateEntry, id).Should().Be(80m);
        AssertEveryStatedBalanceHolds(0m, ledger, [stated], afterLateEntry);
    }

    [Fact]
    public void SeveralStatedBalances_EachAbsorbsOnlyTheDiscrepancyLeftAfterThePreviousOne()
    {
        // No operations at all: the 10th states 100, the 20th states 100 again — the second has nothing to absorb.
        var (tenth, tenthId) = Stated(Sep10, 100m);
        var (twentieth, twentiethId) = Stated(Sep20, 100m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, [], [tenth, twentieth]);

        AmountOf(result, tenthId).Should().Be(100m);
        AmountOf(result, twentiethId).Should().Be(0m);
    }

    [Fact]
    public void SeveralStatedBalances_AreProcessedInDateOrderWhateverTheInputOrder()
    {
        var ledger = new[] { Entry(Sep1, 500m), Entry(Sep10, -200m), Entry(Sep20, 400m) };
        var (tenth, tenthId) = Stated(Sep10, 350m);
        var (twentieth, twentiethId) = Stated(Sep20, 600m);

        var result = StatedBalanceCorrectionCalculator.Calculate(
            0m, ledger.OrderByDescending(x => x.OperationDate).ToList(), [twentieth, tenth]);

        AmountOf(result, tenthId).Should().Be(50m);
        AmountOf(result, twentiethId).Should().Be(-150m);
        result.Select(x => x.Id).Should().Equal([tenthId, twentiethId], "the result follows the date order");
    }

    [Fact]
    public void StatedBalanceBeforeAnyOperation_DoesNotAffectTheBalanceBeforeItsDate()
    {
        var (stated, id) = Stated(Sep1, 70m);
        var ledger = new[] { Entry(Sep10, 30m) };

        var result = StatedBalanceCorrectionCalculator.Calculate(100m, ledger, [stated]);

        AmountOf(result, id).Should().Be(-30m, "100 initial, 70 stated on the 1st");
        AssertEveryStatedBalanceHolds(100m, ledger, [stated], result);
    }

    [Fact]
    public void StatedBalanceEqualToTheComputedOne_YieldsZeroCorrection()
    {
        var (stated, id) = Stated(Sep10, 300m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 500m), Entry(Sep10, -200m)], [stated]);

        AmountOf(result, id).Should().Be(0m);
    }

    [Fact]
    public void NegativeStatedBalance_IsAllowed()
    {
        var (stated, id) = Stated(Sep10, -80m);
        var ledger = new[] { Entry(Sep1, 20m) };

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, ledger, [stated]);

        AmountOf(result, id).Should().Be(-100m);
        AssertEveryStatedBalanceHolds(0m, ledger, [stated], result);
    }

    [Fact]
    public void StatedBalanceWithoutAnyOperation_IsTheDifferenceToTheInitialBalance()
    {
        var (stated, id) = Stated(Sep10, 70m);

        var result = StatedBalanceCorrectionCalculator.Calculate(100m, [], [stated]);

        AmountOf(result, id).Should().Be(-30m);
    }

    [Fact]
    public void NonZeroInitialBalance_IsPartOfTheBalanceWithoutTheCorrection()
    {
        var (stated, id) = Stated(Sep10, 1_000m);
        var ledger = new[] { Entry(Sep1, 200m) };

        var result = StatedBalanceCorrectionCalculator.Calculate(500m, ledger, [stated]);

        AmountOf(result, id).Should().Be(300m);
        AssertEveryStatedBalanceHolds(500m, ledger, [stated], result);
    }

    [Fact]
    public void SignChange_ResultFlipsFromIncomeToExpenseWhenTheLedgerGrows()
    {
        var (stated, id) = Stated(Sep10, 350m);

        var income = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 300m)], [stated]);
        var expense = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 400m)], [stated]);

        AmountOf(income, id).Should().Be(50m);
        AmountOf(expense, id).Should().Be(-50m);
    }

    [Fact]
    public void DeletedOperationsAreNotPartOfTheLedger_SoTheCorrectionGrows()
    {
        // The caller passes only non-deleted entries; removing one from the ledger increases the correction.
        var (stated, id) = Stated(Sep10, 350m);

        var withExpense = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 500m), Entry(Sep10, -200m)], [stated]);
        var withoutExpense = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 500m)], [stated]);

        AmountOf(withExpense, id).Should().Be(50m);
        AmountOf(withoutExpense, id).Should().Be(-150m);
    }

    [Fact]
    public void NoStatedBalances_YieldsNothing()
    {
        StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 500m)], []).Should().BeEmpty();
    }

    [Fact]
    public void FractionalAmounts_AreCalculatedExactly()
    {
        var (stated, id) = Stated(Sep10, 100.10m);

        var result = StatedBalanceCorrectionCalculator.Calculate(0m, [Entry(Sep1, 0.1m), Entry(Sep1, 0.2m)], [stated]);

        AmountOf(result, id).Should().Be(99.80m);
    }
}
