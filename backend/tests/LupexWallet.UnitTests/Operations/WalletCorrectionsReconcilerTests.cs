using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// WalletCorrectionsReconciler (ADR-0003, §6): merges the persisted journal with the unsaved
/// operations of the running command. The journal fake is a real in-memory store that honours the
/// excluded ids, so double counting of an edited operation makes these tests fail.
/// All scenarios use the example of the ADR: +500 on the 1st, -200 and a stated balance of 350 on the 10th.
/// </summary>
public sealed class WalletCorrectionsReconcilerTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);

    private readonly OperationJournalFake _journal = new();
    private readonly WalletId _walletId = WalletId.New();

    private WalletCorrectionsReconciler CreateReconciler() => new(_journal.Repository, _journal.Gateway);

    private static Money Money(decimal amount) => new(amount, OperationJournalFake.Currency);

    private Operation NewIncome(decimal amount, DateOnly date, WalletId? walletId = null) => Operation.Create(
        OperationId.New(), walletId ?? _walletId, OperationTypeId.New(), OperationEffectKind.Income,
        Money(amount), date, adjustmentMode: null, appliedDelta: Money(amount));

    private static void Edit(Operation operation, decimal newAmount, DateOnly date) => operation.Update(
        operation.OperationTypeId!.Value, OperationEffectKind.Income, Money(newAmount), date, adjustmentMode: null, appliedDelta: Money(newAmount));

    /// <summary>Persisted +500 (1st) and -200 (10th), and a persisted correction of the 10th with the given stale amount.</summary>
    private (Operation Income, Operation Expense, Operation Correction) ArrangeAdrJournal(decimal staleCorrectionAmount)
    {
        _journal.ArrangeWallet(_walletId);
        var income = _journal.PersistedOperation(_walletId, Sep1, 500m);
        var expense = _journal.PersistedOperation(_walletId, Sep10, -200m);
        var correction = _journal.PersistedCorrection(_walletId, Sep10, 350m, staleCorrectionAmount);
        return (income, expense, correction);
    }

    // ---- Fast path ----

    [Fact]
    public async Task WalletWithoutCorrections_ReturnsZeroWithoutReadingTheJournalOrTheWallet()
    {
        _journal.ArrangeWallet(_walletId);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        var pending = NewIncome(10m, Sep10);

        var change = await CreateReconciler().ReconcileAsync(_walletId, [pending], CancellationToken.None);

        change.Should().Be(0m);
        _journal.LedgerReads.Should().BeEmpty("without corrections the ledger is not read");
        await _journal.Repository.DidNotReceive().GetCorrectionsAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
        await _journal.Gateway.DidNotReceive().GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnknownWalletWithCorrections_ThrowsWalletReferenceNotFoundException()
    {
        _journal.PersistedCorrection(_walletId, Sep10, 350m, 0m);
        _journal.Gateway.GetAsync(_walletId, Arg.Any<CancellationToken>()).Returns((LupexWallet.Wallets.Application.WalletBalanceInfo?)null);

        var act = () => CreateReconciler().ReconcileAsync(_walletId, [], CancellationToken.None);

        await act.Should().ThrowAsync<WalletReferenceNotFoundException>();
    }

    // ---- Merge of unsaved operations: no double counting ----

    [Fact]
    public async Task AddedOperation_IsTakenFromMemory()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        var added = NewIncome(100m, Sep1); // new, not saved: the journal does not know it

        var change = await CreateReconciler().ReconcileAsync(_walletId, [added], CancellationToken.None);

        // 500 + 100 - 200 = 400 by the operations; 350 stated.
        correction.Amount.Amount.Should().Be(-50m);
        change.Should().Be(-100m, "the correction moved from +50 to -50");
        _journal.LedgerReads.Single().Should().Contain(added.Id);
    }

    [Fact]
    public async Task EditedOperation_IsCountedOnceWithItsNewValue()
    {
        var (income, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        Edit(income, 600m, Sep1); // in memory 600, the "database" still holds 500

        var change = await CreateReconciler().ReconcileAsync(_walletId, [income], CancellationToken.None);

        // 600 - 200 = 400 by the operations (not 500 + 600 - 200); 350 stated.
        correction.Amount.Amount.Should().Be(-50m);
        change.Should().Be(-100m);
    }

    [Fact]
    public async Task EditedOperationMovedBehindTheStatedDate_NoLongerAffectsIt()
    {
        var (income, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        Edit(income, 500m, Sep20); // after the 10th now

        await CreateReconciler().ReconcileAsync(_walletId, [income], CancellationToken.None);

        // Only -200 up to the 10th: 350 - (-200) = 550.
        correction.Amount.Amount.Should().Be(550m);
    }

    [Fact]
    public async Task DeletedOperation_IsRemovedFromTheLedger()
    {
        var (income, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        income.SoftDelete(); // in memory deleted, the "database" row is still active

        var change = await CreateReconciler().ReconcileAsync(_walletId, [income], CancellationToken.None);

        // Only -200 remains: 350 - (-200) = 550.
        correction.Amount.Amount.Should().Be(550m);
        change.Should().Be(500m);
    }

    [Fact]
    public async Task RestoredOperation_ReturnsToTheLedger()
    {
        _journal.ArrangeWallet(_walletId);
        var income = _journal.PersistedOperation(_walletId, Sep1, 500m);
        income.SoftDelete();
        _journal.Persist(income); // the persisted state is "deleted": the ledger read skips it
        var expense = _journal.PersistedOperation(_walletId, Sep10, -200m);
        var correction = _journal.PersistedCorrection(_walletId, Sep10, 350m, 550m);
        income.Restore();

        var change = await CreateReconciler().ReconcileAsync(_walletId, [income], CancellationToken.None);

        correction.Amount.Amount.Should().Be(50m);
        change.Should().Be(-500m);
        _ = expense;
    }

    [Fact]
    public async Task OperationMovedOutOfTheWallet_LeavesItsLedgerAndEntersTheOther()
    {
        var otherWalletId = WalletId.New();
        var (income, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        _journal.ArrangeWallet(otherWalletId);
        var otherCorrection = _journal.PersistedCorrection(otherWalletId, Sep10, 100m, 100m);
        income.MoveToWallet(otherWalletId, income.OperationTypeId!.Value, OperationEffectKind.Income, Money(500m), Sep1, null, Money(500m));

        var oldChange = await CreateReconciler().ReconcileAsync(_walletId, [income], CancellationToken.None);
        var newChange = await CreateReconciler().ReconcileAsync(otherWalletId, [income], CancellationToken.None);

        // Old wallet: only -200 is left, 350 - (-200) = 550.
        correction.Amount.Amount.Should().Be(550m);
        oldChange.Should().Be(500m);
        // New wallet: +500 up to the 10th, 100 stated -> -400.
        otherCorrection.Amount.Amount.Should().Be(-400m);
        newChange.Should().Be(-500m);
    }

    [Fact]
    public async Task OperationOfAnotherWallet_IsNotAddedToTheLedgerOfThisWallet()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        var foreign = NewIncome(1_000m, Sep1, walletId: WalletId.New());

        var change = await CreateReconciler().ReconcileAsync(_walletId, [foreign], CancellationToken.None);

        correction.Amount.Amount.Should().Be(50m);
        change.Should().Be(0m);
    }

    // ---- Corrections from memory ----

    [Fact]
    public async Task NewCorrectionFromMemory_IsRecalculatedEvenThoughTheWalletHadNone()
    {
        _journal.ArrangeWallet(_walletId);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        _journal.PersistedOperation(_walletId, Sep10, -200m);
        var created = Operation.CreateCorrection(OperationId.New(), _walletId, OperationJournalFake.Currency, Sep10, 350m);

        var change = await CreateReconciler().ReconcileAsync(_walletId, [created], CancellationToken.None);

        created.Amount.Amount.Should().Be(50m);
        change.Should().Be(50m, "the new correction starts at zero");
    }

    [Fact]
    public async Task CorrectionPassedAsPendingAndKnownToTheRepository_IsNotDuplicated()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 0m);

        var change = await CreateReconciler().ReconcileAsync(_walletId, [correction], CancellationToken.None);

        correction.Amount.Amount.Should().Be(50m);
        change.Should().Be(50m, "counted once, not twice");
    }

    [Fact]
    public async Task ReplacedStatedBalance_IsRecalculatedWithTheNewValue()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);
        correction.ReplaceStatedBalance(Money(400m));

        var change = await CreateReconciler().ReconcileAsync(_walletId, [correction], CancellationToken.None);

        correction.Amount.Amount.Should().Be(100m);
        change.Should().Be(50m);
    }

    // ---- Total ----

    [Fact]
    public async Task SeveralCorrections_ReturnTheSumOfTheirChangesAndEachIsRecalculatedAfterThePrevious()
    {
        // ADR example, after "+400 on the 20th" and the second statement (600): stale amounts on both dates.
        _journal.ArrangeWallet(_walletId);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        _journal.PersistedOperation(_walletId, Sep10, -200m);
        _journal.PersistedOperation(_walletId, Sep20, 400m);
        var tenth = _journal.PersistedCorrection(_walletId, Sep10, 350m, 0m);
        var twentieth = _journal.PersistedCorrection(_walletId, Sep20, 600m, 0m);

        var change = await CreateReconciler().ReconcileAsync(_walletId, [], CancellationToken.None);

        tenth.Amount.Amount.Should().Be(50m);
        twentieth.Amount.Amount.Should().Be(-150m);
        change.Should().Be(-100m, "+50 and -150");
    }

    [Fact]
    public async Task UnchangedCorrections_ReturnZeroAndRaiseNoEvents()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 50m);

        var change = await CreateReconciler().ReconcileAsync(_walletId, [], CancellationToken.None);

        change.Should().Be(0m);
        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedAmount_RaisesCorrectionRecalculatedForTheLoadedCorrection()
    {
        var (_, _, correction) = ArrangeAdrJournal(staleCorrectionAmount: 0m);

        await CreateReconciler().ReconcileAsync(_walletId, [], CancellationToken.None);

        correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionRecalculated>();
    }
}
