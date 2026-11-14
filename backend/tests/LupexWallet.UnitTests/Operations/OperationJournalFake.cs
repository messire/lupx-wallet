using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// An in-memory stand-in for the persisted state of the operations journal. The repository
/// substitute answers from rows captured by <see cref="Persist"/> — a snapshot taken at that
/// moment, so a later in-memory edit of the same aggregate is NOT visible to the "database"
/// (just like an unsaved EF change). The ledger query honours <c>excludingIds</c> for real; a fake
/// that ignored them would let a reconciler that double-counts an edited operation pass.
/// </summary>
internal sealed class OperationJournalFake
{
    private sealed record Row(OperationId Id, WalletId WalletId, DateOnly Date, decimal Delta, bool IsDeleted);

    private readonly List<Row> _rows = [];
    private readonly List<Operation> _corrections = [];

    public static CurrencyId Currency { get; } = new(Guid.NewGuid());

    public IOperationRepository Repository { get; } = Substitute.For<IOperationRepository>();

    public IWalletBalanceGateway Gateway { get; } = Substitute.For<IWalletBalanceGateway>();

    /// <summary>The excluded id sets of every ledger read, to prove the journal was (not) read.</summary>
    public List<IReadOnlyCollection<OperationId>> LedgerReads { get; } = [];

    /// <summary>The date bound of every ledger read (null = unbounded).</summary>
    public List<DateOnly?> LedgerBounds { get; } = [];

    public OperationJournalFake()
    {
        Repository.GetRegularLedgerAsync(Arg.Any<WalletId>(), Arg.Any<IReadOnlyCollection<OperationId>>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var walletId = call.ArgAt<WalletId>(0);
                var excluded = call.ArgAt<IReadOnlyCollection<OperationId>>(1);
                var throughDate = call.ArgAt<DateOnly?>(2);
                LedgerReads.Add(excluded);
                LedgerBounds.Add(throughDate);
                IReadOnlyList<WalletLedgerEntry> ledger = _rows
                    .Where(r => r.WalletId == walletId && !r.IsDeleted && !excluded.Contains(r.Id)
                        && (throughDate is null || r.Date <= throughDate))
                    .Select(r => new WalletLedgerEntry(r.Date, r.Delta))
                    .ToList();
                return Task.FromResult(ledger);
            });

        Repository.GetCorrectionsAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var walletId = call.ArgAt<WalletId>(0);
                IReadOnlyList<Operation> corrections = _corrections.Where(c => c.WalletId == walletId).OrderBy(c => c.OperationDate).ToList();
                return Task.FromResult(corrections);
            });

        Repository.AnyCorrectionsAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_corrections.Any(c => c.WalletId == call.ArgAt<WalletId>(0))));
    }

    public WalletBalanceInfo ArrangeWallet(
        WalletId walletId, decimal initialBalance = 0m, decimal currentBalance = 0m, DateOnly? accountingStartDate = null)
    {
        var wallet = new WalletBalanceInfo(
            walletId, Currency, new Money(initialBalance, Currency), new Money(currentBalance, Currency),
            accountingStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), IsArchived: false);
        Gateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);
        return wallet;
    }

    /// <summary>Creates a regular operation that is already saved: the "database" knows its current state.</summary>
    public Operation PersistedOperation(WalletId walletId, DateOnly date, decimal delta)
    {
        var operation = Operation.Create(
            OperationId.New(), walletId, OperationTypeId.New(),
            delta >= 0 ? OperationEffectKind.Income : OperationEffectKind.Expense,
            new Money(Math.Abs(delta), Currency), date, adjustmentMode: null, appliedDelta: new Money(delta, Currency));
        operation.ClearDomainEvents();
        Persist(operation);
        return operation;
    }

    /// <summary>Captures the current state of an operation as its persisted state.</summary>
    public void Persist(Operation operation)
    {
        _rows.RemoveAll(r => r.Id == operation.Id);
        _rows.Add(new Row(operation.Id, operation.WalletId, operation.OperationDate, operation.AppliedDelta.Amount, operation.IsDeleted));
    }

    /// <summary>A correction loaded from the database: tracked, with the stored amount and no pending events.</summary>
    public Operation PersistedCorrection(WalletId walletId, DateOnly date, decimal statedBalance, decimal storedAmount)
    {
        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, date, statedBalance);
        correction.ApplyRecalculatedDelta(storedAmount);
        correction.ClearDomainEvents();
        _corrections.Add(correction);
        return correction;
    }
}
