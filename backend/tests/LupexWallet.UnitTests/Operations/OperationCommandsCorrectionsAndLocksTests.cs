using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// Every command that changes the operations of a wallet (ADR-0003, §6; ADR-0001, §4):
/// (1) takes the wallet locks of all affected wallets BEFORE reading the wallet, the operations or
/// the aggregate — the call log records the order; (2) hands the unsaved operations to the
/// corrections reconciler; (3) applies ONE net delta per wallet = the operation's own effect plus the
/// change of the wallet's corrections, and does not call the wallet port at all when the net is zero
/// (the change is absorbed by a later stated balance).
/// The ascending-WalletId order itself is the job of WalletAdvisoryLock and is covered by the integration tests.
/// </summary>
public sealed class OperationCommandsCorrectionsAndLocksTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly ITransferRepository _transferRepository = Substitute.For<ITransferRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IOperationTypeLookup _operationTypeLookup = Substitute.For<IOperationTypeLookup>();
    private readonly IWalletBalanceGateway _gateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletCorrectionsReconciler _reconciler = Substitute.For<IWalletCorrectionsReconciler>();
    private readonly List<string> _calls = [];

    public OperationCommandsCorrectionsAndLocksTests()
    {
        _unitOfWork.When(u => u.AcquireWalletLocksAsync(Arg.Any<IEnumerable<WalletId>>(), Arg.Any<CancellationToken>()))
            .Do(call => _calls.Add("lock:" + string.Join("+", call.Arg<IEnumerable<WalletId>>().Select(x => x.Value).Order())));
        _gateway.When(g => g.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("read-wallet"));
        _operationRepository.When(r => r.GetByIdAsync(Arg.Any<OperationId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("read-operation"));
        _transferRepository.When(r => r.GetByIdAsync(Arg.Any<TransferId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("read-transfer"));
        _operationTypeLookup.When(l => l.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("read-type"));
    }

    private static Money Money(decimal amount) => new(amount, Currency);

    private static WalletBalanceInfo Wallet(WalletId id) => new(
        id, Currency, Money(0m), Money(0m), Today.AddYears(-1), IsArchived: false);

    private WalletId ArrangeWallet(WalletId? id = null)
    {
        var walletId = id ?? WalletId.New();
        _gateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId));
        return walletId;
    }

    private void ArrangeCorrectionsChange(WalletId walletId, decimal change) =>
        _reconciler.ReconcileAsync(walletId, Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>()).Returns(change);

    private static OperationTypeLookupResult ActiveType(string code) =>
        new(OperationTypeId.New(), "Тип", new OperationBehaviorKindId(Guid.NewGuid()), code, IsActive: true);

    private static Operation Income(WalletId walletId, decimal amount) => Operation.Create(
        OperationId.New(), walletId, OperationTypeId.New(), OperationEffectKind.Income,
        Money(amount), Today.AddDays(-1), adjustmentMode: null, appliedDelta: Money(amount));

    private async Task AssertAppliedAsync(WalletId walletId, decimal amount) =>
        await _gateway.Received(1).ApplyDeltaAsync(walletId, Money(amount), Arg.Any<CancellationToken>());

    private async Task AssertNotAppliedAsync(WalletId walletId) =>
        await _gateway.DidNotReceive().ApplyDeltaAsync(walletId, Arg.Any<Money>(), Arg.Any<CancellationToken>());

    // ---- CreateOperation ----

    private CreateOperationCommandHandler CreateCreateHandler() =>
        new(_operationRepository, _unitOfWork, _operationTypeLookup, _gateway, _reconciler);

    [Fact]
    public async Task CreateOperation_AppliesItsEffectPlusTheCorrectionsChangeAsOneDelta()
    {
        var walletId = ArrangeWallet();
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        ArrangeCorrectionsChange(walletId, -30m);

        await CreateCreateHandler().Handle(
            new CreateOperationCommand(walletId.Value, Guid.NewGuid(), 100m, Today, null), CancellationToken.None);

        await AssertAppliedAsync(walletId, 70m);
        await _gateway.Received(1).ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileAsync(
            walletId, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Count == 1 && !x.Single().IsCorrection), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOperation_FullyAbsorbedByALaterStatedBalance_DoesNotCallTheWalletPort()
    {
        var walletId = ArrangeWallet();
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        ArrangeCorrectionsChange(walletId, -100m);

        await CreateCreateHandler().Handle(
            new CreateOperationCommand(walletId.Value, Guid.NewGuid(), 100m, Today, null), CancellationToken.None);

        await AssertNotAppliedAsync(walletId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateOperation_TakesTheWalletLockBeforeAnyRead()
    {
        var walletId = ArrangeWallet();
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));

        await CreateCreateHandler().Handle(
            new CreateOperationCommand(walletId.Value, Guid.NewGuid(), 100m, Today, null), CancellationToken.None);

        _calls.Should().StartWith($"lock:{walletId.Value}");
        _calls.Count(c => c.StartsWith("lock:")).Should().Be(1);
    }

    // ---- UpdateOperation ----

    private UpdateOperationCommandHandler CreateUpdateHandler() =>
        new(_operationRepository, _unitOfWork, _operationTypeLookup, _gateway, _reconciler);

    [Fact]
    public async Task UpdateOperation_SameWallet_AppliesIncrementalDeltaPlusCorrectionsChange()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 100m);
        _operationRepository.Found(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        ArrangeCorrectionsChange(walletId, 20m);

        await CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, walletId.Value, Guid.NewGuid(), 150m, operation.OperationDate, null), CancellationToken.None);

        await AssertAppliedAsync(walletId, 70m); // 150 - 100 + 20
        await _reconciler.Received(1).ReconcileAsync(
            walletId, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Single() == operation), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOperation_SameWallet_NetZero_DoesNotCallTheWalletPort()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 100m);
        _operationRepository.Found(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        ArrangeCorrectionsChange(walletId, -50m); // a later stated balance absorbs the +50 edit

        await CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, walletId.Value, Guid.NewGuid(), 150m, operation.OperationDate, null), CancellationToken.None);

        await AssertNotAppliedAsync(walletId);
    }

    [Fact]
    public async Task UpdateOperation_MoveToAnotherWallet_AppliesOneNetDeltaPerWallet()
    {
        var oldWalletId = ArrangeWallet();
        var newWalletId = ArrangeWallet();
        var operation = Income(oldWalletId, 100m);
        _operationRepository.Found(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        ArrangeCorrectionsChange(oldWalletId, 30m);
        ArrangeCorrectionsChange(newWalletId, -250m);

        await CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 250m, operation.OperationDate, null), CancellationToken.None);

        await AssertAppliedAsync(oldWalletId, -70m);   // reversal -100 + corrections +30
        await AssertNotAppliedAsync(newWalletId);       // +250 - 250 = 0
        await _reconciler.Received(1).ReconcileAsync(oldWalletId, Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileAsync(newWalletId, Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOperation_MoveToAnotherWallet_LocksBothWalletsBeforeLoadingTheAggregateOrReadingWallets()
    {
        var oldWalletId = ArrangeWallet();
        var newWalletId = ArrangeWallet();
        var operation = Income(oldWalletId, 100m);
        _operationRepository.Found(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));

        await CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 100m, operation.OperationDate, null), CancellationToken.None);

        var expectedLock = "lock:" + string.Join("+", new[] { oldWalletId.Value, newWalletId.Value }.Order());
        _calls.Should().StartWith(expectedLock);
        _calls.IndexOf("read-operation").Should().BeGreaterThan(0);
        _calls.IndexOf("read-wallet").Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateOperation_Correction_ThrowsCorrectionNotEditableBeforeAnyBalanceChange()
    {
        var walletId = ArrangeWallet();
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Today, 100m);
        _operationRepository.Found(correction);

        var act = () => CreateUpdateHandler().Handle(
            new UpdateOperationCommand(correction.Id.Value, walletId.Value, Guid.NewGuid(), 1m, Today, null), CancellationToken.None);

        await act.Should().ThrowAsync<CorrectionNotEditableException>();
        await _gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- DeleteOperation / RestoreOperation ----

    [Fact]
    public async Task DeleteOperation_AppliesTheReversalPlusCorrectionsChangeAsOneDelta()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 70m);
        _operationRepository.Found(operation);
        ArrangeCorrectionsChange(walletId, 20m);

        await new DeleteOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new DeleteOperationCommand(operation.Id.Value), CancellationToken.None);

        await AssertAppliedAsync(walletId, -50m);
        await _reconciler.Received(1).ReconcileAsync(
            walletId, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Single() == operation && x.Single().IsDeleted), Arg.Any<CancellationToken>());
        _calls.Should().StartWith($"lock:{walletId.Value}", "the lock precedes the aggregate load");
    }

    [Fact]
    public async Task DeleteOperation_NetZero_DoesNotCallTheWalletPort()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 70m);
        _operationRepository.Found(operation);
        ArrangeCorrectionsChange(walletId, 70m);

        await new DeleteOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new DeleteOperationCommand(operation.Id.Value), CancellationToken.None);

        await AssertNotAppliedAsync(walletId);
    }

    [Fact]
    public async Task DeleteOperation_Correction_ThrowsCorrectionNotEditableBeforeTouchingTheWallet()
    {
        var walletId = ArrangeWallet();
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Today, 100m);
        _operationRepository.Found(correction);

        var act = () => new DeleteOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new DeleteOperationCommand(correction.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<CorrectionNotEditableException>();
        await _reconciler.DidNotReceive().ReconcileAsync(
            Arg.Any<WalletId>(), Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>());
        await AssertNotAppliedAsync(walletId);
    }

    [Fact]
    public async Task RestoreOperation_AppliesTheEffectPlusCorrectionsChangeAsOneDelta()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 70m);
        operation.SoftDelete();
        _operationRepository.Found(operation);
        ArrangeCorrectionsChange(walletId, -20m);

        await new RestoreOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await AssertAppliedAsync(walletId, 50m);
        await _reconciler.Received(1).ReconcileAsync(
            walletId, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Single() == operation && !x.Single().IsDeleted), Arg.Any<CancellationToken>());
        _calls.Should().StartWith($"lock:{walletId.Value}");
    }

    [Fact]
    public async Task RestoreOperation_Correction_ThrowsCorrectionNotEditableBeforeTouchingTheWallet()
    {
        var walletId = ArrangeWallet();
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Today, 100m);
        _operationRepository.Found(correction);

        var act = () => new RestoreOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new RestoreOperationCommand(correction.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<CorrectionNotEditableException>();
        await AssertNotAppliedAsync(walletId);
    }

    // ---- The operation was moved to another wallet between the lookup and the lock ----

    /// <summary>The lookup answers with <paramref name="lockedWalletId"/> while the loaded aggregate already belongs to another wallet.</summary>
    private Operation ArrangeOperationMovedAwayFrom(WalletId lockedWalletId)
    {
        var operation = Income(WalletId.New(), 100m);
        _operationRepository.FindWalletIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(lockedWalletId);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        return operation;
    }

    private async Task AssertNothingChangedAsync()
    {
        await _gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOperation_MovedToAnotherWalletBeforeTheLockWasTaken_ThrowsRecordModifiedConcurrently()
    {
        var lockedWalletId = ArrangeWallet();
        var operation = ArrangeOperationMovedAwayFrom(lockedWalletId);

        var act = () => CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, lockedWalletId.Value, Guid.NewGuid(), 1m, Today, null), CancellationToken.None);

        await act.Should().ThrowAsync<RecordModifiedConcurrentlyException>();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task DeleteOperation_MovedToAnotherWalletBeforeTheLockWasTaken_ThrowsRecordModifiedConcurrently()
    {
        var operation = ArrangeOperationMovedAwayFrom(ArrangeWallet());

        var act = () => new DeleteOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new DeleteOperationCommand(operation.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<RecordModifiedConcurrentlyException>();
        operation.IsDeleted.Should().BeFalse();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task RestoreOperation_MovedToAnotherWalletBeforeTheLockWasTaken_ThrowsRecordModifiedConcurrently()
    {
        var operation = ArrangeOperationMovedAwayFrom(ArrangeWallet());
        operation.SoftDelete();

        var act = () => new RestoreOperationCommandHandler(_operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<RecordModifiedConcurrentlyException>();
        operation.IsDeleted.Should().BeTrue();
        await AssertNothingChangedAsync();
    }

    // ---- A rejected adjustmentMode is reported after the checks of the operation itself ----

    [Fact]
    public async Task UpdateOperation_RejectedAdjustmentModeOfARegularOperation_IsThrownBeforeAnyBalanceChange()
    {
        var walletId = ArrangeWallet();
        var operation = Income(walletId, 100m);
        _operationRepository.Found(operation);

        var act = () => CreateUpdateHandler().Handle(
            new UpdateOperationCommand(operation.Id.Value, walletId.Value, Guid.NewGuid(), 1m, Today, null, new AbsoluteAdjustmentNotSupportedException()),
            CancellationToken.None);

        await act.Should().ThrowAsync<AbsoluteAdjustmentNotSupportedException>();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task UpdateOperation_RejectedAdjustmentModeOfACorrection_ThrowsCorrectionNotEditableFirst()
    {
        var walletId = ArrangeWallet();
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Today, 100m);
        _operationRepository.Found(correction);

        var act = () => CreateUpdateHandler().Handle(
            new UpdateOperationCommand(correction.Id.Value, walletId.Value, Guid.NewGuid(), 1m, Today, null, new AbsoluteAdjustmentNotSupportedException()),
            CancellationToken.None);

        await act.Should().ThrowAsync<CorrectionNotEditableException>();
    }

    [Fact]
    public async Task UpdateOperation_RejectedAdjustmentModeOfAMissingOperation_StaysNotFound()
    {
        _operationRepository.FindWalletIdAsync(Arg.Any<OperationId>(), Arg.Any<CancellationToken>()).Returns((WalletId?)null);

        var act = () => CreateUpdateHandler().Handle(
            new UpdateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1m, Today, null, new AbsoluteAdjustmentNotSupportedException()),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---- Transfers ----

    private (Transfer Transfer, Operation Source, Operation Target) NewTransfer(WalletId source, WalletId target, decimal amount)
    {
        var created = Transfer.Create(TransferId.New(), source, target, OperationTypeId.New(), Money(amount), Today.AddDays(-1));
        _transferRepository.Found(created.Item1);
        _operationRepository.GetByIdAsync(created.Item1.SourceOperationId, Arg.Any<CancellationToken>()).Returns(created.Item2);
        _operationRepository.GetByIdAsync(created.Item1.TargetOperationId, Arg.Any<CancellationToken>()).Returns(created.Item3);
        return created;
    }

    [Fact]
    public async Task CreateTransfer_AppliesOneNetDeltaPerWalletAndLocksBothWalletsFirst()
    {
        var source = ArrangeWallet();
        var target = ArrangeWallet();
        _operationTypeLookup.FindActiveByBehaviorKindCodeAsync("Transfer", Arg.Any<CancellationToken>()).Returns(ActiveType("Transfer"));
        ArrangeCorrectionsChange(source, 10m);
        ArrangeCorrectionsChange(target, -60m);

        await new CreateTransferCommandHandler(_operationRepository, _transferRepository, _unitOfWork, _operationTypeLookup, _gateway, _reconciler)
            .Handle(new CreateTransferCommand(source.Value, target.Value, 60m, Today), CancellationToken.None);

        await AssertAppliedAsync(source, -50m);  // -60 + 10
        await AssertNotAppliedAsync(target);      // +60 - 60
        _calls.Should().StartWith("lock:" + string.Join("+", new[] { source.Value, target.Value }.Order()));
        await _reconciler.Received(1).ReconcileAsync(
            source, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Count == 2), Arg.Any<CancellationToken>());
        await _reconciler.Received(1).ReconcileAsync(
            target, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteTransfer_AppliesOneNetDeltaPerWalletAndLocksBothWalletsFirst()
    {
        var source = ArrangeWallet();
        var target = ArrangeWallet();
        var (transfer, _, _) = NewTransfer(source, target, 60m);
        ArrangeCorrectionsChange(source, -10m);
        ArrangeCorrectionsChange(target, 60m);

        await new DeleteTransferCommandHandler(_transferRepository, _operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new DeleteTransferCommand(transfer.Id.Value), CancellationToken.None);

        await AssertAppliedAsync(source, 50m);   // reversal +60 - 10
        await AssertNotAppliedAsync(target);      // reversal -60 + 60
        _calls.Should().StartWith("lock:" + string.Join("+", new[] { source.Value, target.Value }.Order()));
        _calls.IndexOf("read-transfer").Should().BeGreaterThan(0);
        await _reconciler.Received(1).ReconcileAsync(
            source, Arg.Is<IReadOnlyCollection<Operation>>(x => x.All(o => o.IsDeleted)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreTransfer_AppliesOneNetDeltaPerWalletAndLocksBothWalletsFirst()
    {
        var source = ArrangeWallet();
        var target = ArrangeWallet();
        var (transfer, sourceLeg, targetLeg) = NewTransfer(source, target, 60m);
        transfer.SoftDelete(sourceLeg, targetLeg);
        ArrangeCorrectionsChange(source, 5m);
        ArrangeCorrectionsChange(target, -5m);

        await new RestoreTransferCommandHandler(_transferRepository, _operationRepository, _unitOfWork, _gateway, _reconciler)
            .Handle(new RestoreTransferCommand(transfer.Id.Value), CancellationToken.None);

        await AssertAppliedAsync(source, -55m);  // -60 + 5
        await AssertAppliedAsync(target, 55m);   // +60 - 5
        _calls.Should().StartWith("lock:" + string.Join("+", new[] { source.Value, target.Value }.Order()));
        await _reconciler.Received(1).ReconcileAsync(
            source, Arg.Is<IReadOnlyCollection<Operation>>(x => x.All(o => !o.IsDeleted)), Arg.Any<CancellationToken>());
    }
}
