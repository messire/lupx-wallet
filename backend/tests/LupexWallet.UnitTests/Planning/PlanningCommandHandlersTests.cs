using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Planning.Application;
using LupexWallet.Planning.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;
using static LupexWallet.UnitTests.Planning.PlanningTestData;

namespace LupexWallet.UnitTests.Planning;

/// <summary>Обработчики команд Planning: проверки ссылок, исполнение через IOperationRecorder, удаление/завершение.</summary>
public sealed class PlanningCommandHandlersTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IPlannedTransactionRepository _repository = Substitute.For<IPlannedTransactionRepository>();
    private readonly IPlanningUnitOfWork _unitOfWork = Substitute.For<IPlanningUnitOfWork>();
    private readonly IOperationTypeLookup _operationTypes = Substitute.For<IOperationTypeLookup>();
    private readonly ICurrencyLookup _currencies = Substitute.For<ICurrencyLookup>();
    private readonly IWalletBalanceGateway _wallets = Substitute.For<IWalletBalanceGateway>();
    private readonly IOperationFactsLookup _facts = Substitute.For<IOperationFactsLookup>();
    private readonly IOperationRecorder _recorder = Substitute.For<IOperationRecorder>();

    public PlanningCommandHandlersTests()
    {
        _currencies.GetAsync(Currency, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Currency, "USD"));
        _facts.GetByIdsAsync(Arg.Any<IReadOnlyCollection<OperationId>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private PlannedTransactionReferences References => new(_operationTypes, _currencies, _wallets);

    private OperationTypeId OperationType(string behavior, bool isActive = true)
    {
        var id = new OperationTypeId(Guid.NewGuid());
        _operationTypes.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new OperationTypeLookupResult(id, "Тип", new OperationBehaviorKindId(Guid.NewGuid()), behavior, isActive));
        return id;
    }

    private WalletId Wallet(CurrencyId currencyId, DateOnly? accountingStartDate = null)
    {
        var id = WalletId.New();
        _wallets.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new WalletBalanceInfo(
            id, currencyId, new Money(0m, currencyId), new Money(0m, currencyId), accountingStartDate ?? Today.AddYears(-1), false));
        return id;
    }

    private PlannedTransaction StoredPlan(OperationTypeId operationTypeId, DateOnly start, WalletId? walletId = null, decimal amount = 100m)
    {
        var plan = PlannedTransaction.Create(
            PlannedTransactionId.New(), operationTypeId, PlannedDirection.Expense, amount, Currency, walletId, null, start,
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, null));
        _repository.GetForUpdateAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        return plan;
    }

    private ExecutePlannedOccurrenceCommandHandler ExecuteHandler() =>
        new(_repository, _unitOfWork, new PlannedOccurrenceReader(_facts), References, _recorder);

    private void RecorderReturns(WalletId walletId, decimal amount, DateOnly date, OperationId operationId) =>
        _recorder.RecordAsync(walletId, Arg.Any<OperationTypeId>(), amount, date, Arg.Any<CancellationToken>())
            .Returns(new RecordedOperation(operationId, walletId, Currency, amount, date));

    [Fact]
    public async Task Create_ResolvesDirectionFromOperationType()
    {
        var handler = new CreatePlannedTransactionCommandHandler(_repository, _unitOfWork, References);
        var income = OperationType("Income");

        var dto = await handler.Handle(
            new CreatePlannedTransactionCommand(income.Value, 10m, Currency.Value, null, "Зарплата", Today, null), CancellationToken.None);

        dto.Direction.Should().Be(PlannedDirection.Income);
        _repository.Received(1).Add(Arg.Any<PlannedTransaction>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Transfer")]
    [InlineData("Adjustment")]
    public async Task Create_NonIncomeExpenseType_Throws(string behavior)
    {
        var handler = new CreatePlannedTransactionCommandHandler(_repository, _unitOfWork, References);
        var type = OperationType(behavior);

        await FluentActions.Awaiting(() => handler.Handle(
                new CreatePlannedTransactionCommand(type.Value, 10m, Currency.Value, null, null, Today, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedOperationTypeBehaviorNotSupportedException>();
    }

    [Fact]
    public async Task Create_InactiveType_UnknownCurrency_WalletInOtherCurrency_Throw()
    {
        var handler = new CreatePlannedTransactionCommandHandler(_repository, _unitOfWork, References);

        await FluentActions.Awaiting(() => handler.Handle(
                new CreatePlannedTransactionCommand(OperationType("Expense", isActive: false).Value, 10m, Currency.Value, null, null, Today, null),
                CancellationToken.None))
            .Should().ThrowAsync<PlannedOperationTypeInactiveException>();

        await FluentActions.Awaiting(() => handler.Handle(
                new CreatePlannedTransactionCommand(OperationType("Expense").Value, 10m, Guid.NewGuid(), null, null, Today, null),
                CancellationToken.None))
            .Should().ThrowAsync<PlannedCurrencyNotFoundException>();

        var otherCurrencyWallet = Wallet(new CurrencyId(Guid.NewGuid()));
        await FluentActions.Awaiting(() => handler.Handle(
                new CreatePlannedTransactionCommand(OperationType("Expense").Value, 10m, Currency.Value, otherCurrencyWallet.Value, null, Today, null),
                CancellationToken.None))
            .Should().ThrowAsync<PlannedWalletCurrencyMismatchException>();

        _repository.DidNotReceive().Add(Arg.Any<PlannedTransaction>());
    }

    [Fact]
    public async Task Execute_Defaults_PlanWallet_RemainingAmount_EffectiveDate()
    {
        var walletId = Wallet(Currency);
        var plan = StoredPlan(OperationType("Expense"), Today.AddMonths(-1), walletId, amount: 100m);
        var operationId = OperationId.New();
        RecorderReturns(walletId, 100m, Today.AddMonths(-1), operationId);

        var result = await ExecuteHandler().Handle(
            new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today.AddMonths(-1), null, null, null), CancellationToken.None);

        result.OperationId.Should().Be(operationId.Value);
        result.Occurrence.Status.Should().Be(PlannedOccurrenceStatus.Executed);
        plan.Executions.Should().ContainSingle().Which.OperationId.Should().Be(operationId);
        await _recorder.Received(1).RecordAsync(walletId, plan.OperationTypeId, 100m, Today.AddMonths(-1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_Partial_ThenRemainderDefault()
    {
        var walletId = Wallet(Currency);
        var date = Today.AddMonths(-2);
        var plan = StoredPlan(OperationType("Expense"), date, walletId, amount: 100m);
        RecorderReturns(walletId, 30m, Today, OperationId.New());
        RecorderReturns(walletId, 70m, date, OperationId.New());

        var first = await ExecuteHandler().Handle(
            new ExecutePlannedOccurrenceCommand(plan.Id.Value, date, null, 30m, Today), CancellationToken.None);
        first.Occurrence.Status.Should().Be(PlannedOccurrenceStatus.PartiallyExecuted);
        first.Occurrence.ExpectedAmount.Should().Be(70m);

        var second = await ExecuteHandler().Handle(
            new ExecutePlannedOccurrenceCommand(plan.Id.Value, date, null, null, null), CancellationToken.None);
        second.Occurrence.Status.Should().Be(PlannedOccurrenceStatus.Executed);
    }

    [Fact]
    public async Task Execute_WithoutAnyWallet_Throws()
    {
        var plan = StoredPlan(OperationType("Expense"), Today);

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedExecutionWalletRequiredException>();
    }

    [Fact]
    public async Task Execute_WalletInOtherCurrency_Throws_WithoutRecording()
    {
        var plan = StoredPlan(OperationType("Expense"), Today);
        var otherWallet = Wallet(new CurrencyId(Guid.NewGuid()));

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today, otherWallet.Value, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedWalletCurrencyMismatchException>();
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Execute_FutureDefaultDate_OrBeforeAccountingStart_Throws()
    {
        var walletId = Wallet(Currency, accountingStartDate: Today.AddDays(-10));
        var start = Today.AddMonths(-1);
        var plan = StoredPlan(OperationType("Expense"), start, walletId);

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, start.AddMonths(2), null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedExecutionDateOutOfRangeException>();

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today.AddMonths(-1), null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedExecutionDateOutOfRangeException>();
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task Execute_DeactivatedOperationType_Throws()
    {
        var walletId = Wallet(Currency);
        var plan = StoredPlan(OperationType("Expense", isActive: false), Today, walletId);

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedOperationTypeInactiveException>();
    }

    [Fact]
    public async Task Execute_UnknownPlanOrDate_IsNotFound()
    {
        var plan = StoredPlan(OperationType("Expense"), Today);

        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(Guid.NewGuid(), Today, null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedTransactionNotFoundException>();
        await FluentActions.Awaiting(() => ExecuteHandler().Handle(
                new ExecutePlannedOccurrenceCommand(plan.Id.Value, Today.AddDays(1), null, null, null), CancellationToken.None))
            .Should().ThrowAsync<PlannedOccurrenceNotFoundException>();
    }

    [Fact]
    public async Task Delete_WithoutExecutions_Removes_WithExecutions_Finishes()
    {
        var handler = new DeletePlannedTransactionCommandHandler(_repository, _unitOfWork);

        var empty = StoredPlan(OperationType("Expense"), Today);
        (await handler.Handle(new DeletePlannedTransactionCommand(empty.Id.Value), CancellationToken.None)).FinishedPlan.Should().BeNull();
        _repository.Received(1).Remove(empty);

        var executed = StoredPlan(OperationType("Expense"), Today);
        Execute(executed, Today, 10m);
        var result = await handler.Handle(new DeletePlannedTransactionCommand(executed.Id.Value), CancellationToken.None);
        result.FinishedPlan!.Status.Should().Be(PlannedTransactionStatus.Finished);
        _repository.DidNotReceive().Remove(executed);
    }

    [Fact]
    public async Task OccurrenceRange_Invalid_Throws()
    {
        var handler = new ListPlannedOccurrencesQueryHandler(_repository, new PlannedOccurrenceReader(_facts));

        await FluentActions.Awaiting(() => handler.Handle(new ListPlannedOccurrencesQuery(Today, Today.AddDays(-1), null), CancellationToken.None))
            .Should().ThrowAsync<PlannedOccurrenceRangeInvalidException>();
        await FluentActions.Awaiting(() => handler.Handle(
                new ListPlannedOccurrencesQuery(Today, Today.AddDays(ListPlannedOccurrencesQuery.MaxRangeDays), null), CancellationToken.None))
            .Should().ThrowAsync<PlannedOccurrenceRangeInvalidException>();
    }
}
