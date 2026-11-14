using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// ReconcileWalletCorrectionsCommandHandler (ADR-0003, §6): recalculates the corrections of a wallet
/// and heals CurrentBalance to the invariant (initial balance + the effect of all non-deleted
/// operations, corrections included). Run at startup after the data of earlier formats was converted.
/// The real reconciler runs over the in-memory journal.
/// </summary>
public sealed class ReconcileWalletCorrectionsCommandHandlerTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep10 = new(2026, 9, 10);

    private readonly OperationJournalFake _journal = new();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly WalletId _walletId = WalletId.New();

    private ReconcileWalletCorrectionsCommandHandler CreateHandler() => new(
        _journal.Repository, _unitOfWork, _journal.Gateway, new WalletCorrectionsReconciler(_journal.Repository, _journal.Gateway));

    private static Money Money(decimal amount) => new(amount, OperationJournalFake.Currency);

    [Fact]
    public async Task StaleCorrectionAndBalance_RecalculatesTheCorrectionAndHealsCurrentBalance()
    {
        // Initial 100, +500, -200 and a stated 350 on the 10th: the correction must be 350 - 400 = -50 and
        // CurrentBalance must be 350. The data is stale: correction 0, CurrentBalance 400 (after an old format).
        _journal.ArrangeWallet(_walletId, initialBalance: 100m, currentBalance: 400m);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        _journal.PersistedOperation(_walletId, Sep10, -200m);
        var correction = _journal.PersistedCorrection(_walletId, Sep10, 350m, 0m);

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        correction.Amount.Amount.Should().Be(-50m);
        correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionRecalculated>();
        await _journal.Gateway.Received(1).ApplyDeltaAsync(_walletId, Money(-50m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectionRightButBalanceWrong_OnlyTheBalanceIsHealed()
    {
        _journal.ArrangeWallet(_walletId, initialBalance: 0m, currentBalance: 999m);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        _journal.PersistedOperation(_walletId, Sep10, -200m);
        var correction = _journal.PersistedCorrection(_walletId, Sep10, 350m, 50m);

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        correction.DomainEvents.Should().BeEmpty();
        await _journal.Gateway.Received(1).ApplyDeltaAsync(_walletId, Money(350m - 999m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EverythingAlreadyAgrees_ChangesNothingRaisesNoEventsAndDoesNotTouchTheWallet()
    {
        _journal.ArrangeWallet(_walletId, initialBalance: 0m, currentBalance: 350m);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        _journal.PersistedOperation(_walletId, Sep10, -200m);
        var correction = _journal.PersistedCorrection(_walletId, Sep10, 350m, 50m);

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        correction.DomainEvents.Should().BeEmpty();
        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletWithoutCorrections_IsLeftAloneEvenIfItsBalanceDiffersFromTheLedger()
    {
        // A wallet without corrections is outside the reconciliation (it is not part of the stated-balance model).
        _journal.ArrangeWallet(_walletId, initialBalance: 0m, currentBalance: 12345m);
        _journal.PersistedOperation(_walletId, Sep1, 500m);

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _journal.LedgerReads.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownWallet_DoesNothing()
    {
        _journal.PersistedCorrection(_walletId, Sep10, 350m, 50m);
        _journal.Gateway.GetAsync(_walletId, Arg.Any<CancellationToken>()).Returns((LupexWallet.Wallets.Application.WalletBalanceInfo?)null);

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SoftDeletedOperations_AreNotPartOfTheExpectedBalance()
    {
        _journal.ArrangeWallet(_walletId, initialBalance: 0m, currentBalance: 350m);
        _journal.PersistedOperation(_walletId, Sep1, 500m);
        var deleted = _journal.PersistedOperation(_walletId, Sep10, -200m);
        deleted.SoftDelete();
        _journal.Persist(deleted);
        _journal.PersistedCorrection(_walletId, Sep10, 350m, 50m); // stale: without the deleted -200 it must be -150

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        // Correction -150; expected balance 0 + 500 - 150 = 350: the balance already agrees, nothing to apply.
        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TakesTheWalletLockBeforeReadingTheWallet()
    {
        _journal.ArrangeWallet(_walletId);
        var calls = new List<string>();
        _unitOfWork.When(u => u.AcquireWalletLocksAsync(Arg.Any<IEnumerable<WalletId>>(), Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("lock"));
        _journal.Gateway.When(g => g.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())).Do(_ => calls.Add("wallet"));

        await CreateHandler().Handle(new ReconcileWalletCorrectionsCommand(_walletId.Value), CancellationToken.None);

        calls.Should().StartWith("lock");
    }
}
