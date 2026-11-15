using FluentAssertions;
using LupexWallet.Debts.Application;
using LupexWallet.Debts.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Debts;

/// <summary>
/// Repayment command (ADR-0010 §3, B4): validation before the expense is created, counting of
/// linked operations from facts (a soft-deleted one does not count), atomic linking.
/// </summary>
public sealed class RecordDebtRepaymentCommandHandlerTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IDebtRepository _debts = Substitute.For<IDebtRepository>();
    private readonly IDebtsUnitOfWork _unitOfWork = Substitute.For<IDebtsUnitOfWork>();
    private readonly IOperationFactsLookup _facts = Substitute.For<IOperationFactsLookup>();
    private readonly IOperationTypeLookup _types = Substitute.For<IOperationTypeLookup>();
    private readonly IWalletBalanceGateway _wallets = Substitute.For<IWalletBalanceGateway>();
    private readonly IOperationRecorder _recorder = Substitute.For<IOperationRecorder>();

    private readonly WalletId _walletId = WalletId.New();
    private readonly OperationTypeId _expenseType = OperationTypeId.New();
    private readonly Debt _debt = Debt.Create(DebtId.New(), CreditorId.New(), "Долг", new Money(100m, Usd), Today.AddDays(-10));

    public RecordDebtRepaymentCommandHandlerTests()
    {
        _debts.GetForUpdateAsync(_debt.Id, Arg.Any<CancellationToken>()).Returns(_debt);
        _types.GetAsync(_expenseType, Arg.Any<CancellationToken>())
            .Returns(new OperationTypeLookupResult(_expenseType, "Погашение", new OperationBehaviorKindId(Guid.NewGuid()), "Expense", true));
        GivenWallet(Usd);
        _facts.GetByIdsAsync(Arg.Any<IReadOnlyCollection<OperationId>>(), Arg.Any<CancellationToken>()).Returns(ci =>
            ci.Arg<IReadOnlyCollection<OperationId>>().Select(id => _knownFacts.GetValueOrDefault(id)).OfType<OperationFact>().ToList());
        _recorder.RecordAsync(_walletId, _expenseType, Arg.Any<decimal>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var id = OperationId.New();
                _knownFacts[id] = Fact(id, ci.ArgAt<decimal>(2), deleted: false);
                return new RecordedOperation(id, _walletId, Usd, ci.ArgAt<decimal>(2), ci.ArgAt<DateOnly>(3));
            });
    }

    private readonly Dictionary<OperationId, OperationFact> _knownFacts = [];

    private void GivenWallet(CurrencyId currency) =>
        _wallets.GetAsync(_walletId, Arg.Any<CancellationToken>()).Returns(new WalletBalanceInfo(
            _walletId, currency, new Money(0m, currency), new Money(1000m, currency), Today.AddYears(-1), IsArchived: false));

    private OperationFact Fact(OperationId id, decimal amount, bool deleted) =>
        new(id, _walletId, _expenseType, Usd, amount, -amount, Today, IsTransferLeg: false, IsCorrection: false, IsDeleted: deleted);

    private RecordDebtRepaymentCommandHandler Handler() => new(
        _debts, _unitOfWork, new PaymentFactsReader(_facts), new ExpensePaymentRecorder(_types, _wallets, _recorder));

    private RecordDebtRepaymentCommand Command(decimal amount, DateOnly? date = null, OperationTypeId? type = null) =>
        new(_debt.Id.Value, _walletId.Value, (type ?? _expenseType).Value, amount, date ?? Today);

    [Fact]
    public async Task Partial_RecordsExpenseLinksAndSaves()
    {
        var dto = await Handler().Handle(Command(40m), CancellationToken.None);

        await _recorder.Received(1).RecordAsync(_walletId, _expenseType, 40m, Today, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        dto.RepaidAmount.Should().Be(40m);
        dto.RemainingAmount.Should().Be(60m);
        dto.Status.Should().Be("Active");
        dto.Repayments.Should().ContainSingle(r => r.IsCounted);
    }

    [Fact]
    public async Task FullRepayment_MakesDebtRepaid()
    {
        var dto = await Handler().Handle(Command(100m), CancellationToken.None);
        dto.Status.Should().Be("Repaid");
        dto.RemainingAmount.Should().Be(0m);
    }

    [Fact]
    public async Task SoftDeletedEarlierRepayment_DoesNotCount()
    {
        var deletedOperation = OperationId.New();
        _debt.RecordRepayment(deletedOperation, _walletId, new Money(100m, Usd), Today, countedRepaid: 0m);
        _knownFacts[deletedOperation] = Fact(deletedOperation, 100m, deleted: true);

        var dto = await Handler().Handle(Command(100m), CancellationToken.None);

        dto.Status.Should().Be("Repaid");
        dto.Repayments.Should().HaveCount(2);
        dto.Repayments.Single(r => r.OperationId == deletedOperation.Value).IsCounted.Should().BeFalse();
    }

    [Fact]
    public async Task AmountAboveRemaining_ThrowsBeforeRecordingTheExpense()
    {
        var act = () => Handler().Handle(Command(100.01m), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentExceedsRemainingException>();
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task WalletInOtherCurrency_Throws()
    {
        GivenWallet(new CurrencyId(Guid.NewGuid()));
        var act = () => Handler().Handle(Command(10m), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentCurrencyMismatchException>();
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task NonExpenseOperationType_Throws()
    {
        var incomeType = OperationTypeId.New();
        _types.GetAsync(incomeType, Arg.Any<CancellationToken>())
            .Returns(new OperationTypeLookupResult(incomeType, "Зарплата", new OperationBehaviorKindId(Guid.NewGuid()), "Income", true));

        var act = () => Handler().Handle(Command(10m, type: incomeType), CancellationToken.None);
        await act.Should().ThrowAsync<PaymentOperationTypeNotExpenseException>();
    }

    [Fact]
    public async Task InactiveOrUnknownOperationType_Throws()
    {
        var inactive = OperationTypeId.New();
        _types.GetAsync(inactive, Arg.Any<CancellationToken>())
            .Returns(new OperationTypeLookupResult(inactive, "Старый", new OperationBehaviorKindId(Guid.NewGuid()), "Expense", false));

        await Handler().Invoking(h => h.Handle(Command(10m, type: inactive), CancellationToken.None))
            .Should().ThrowAsync<PaymentOperationTypeInactiveException>();
        await Handler().Invoking(h => h.Handle(Command(10m, type: OperationTypeId.New()), CancellationToken.None))
            .Should().ThrowAsync<PaymentOperationTypeNotFoundException>();
    }

    [Fact]
    public async Task FutureDate_Throws()
    {
        var act = () => Handler().Handle(Command(10m, date: Today.AddDays(1)), CancellationToken.None);
        await act.Should().ThrowAsync<PaymentDateOutOfRangeException>();
    }

    [Fact]
    public async Task UnknownDebt_ThrowsKeyNotFound()
    {
        var act = () => Handler().Handle(Command(10m) with { DebtId = Guid.NewGuid() }, CancellationToken.None);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
