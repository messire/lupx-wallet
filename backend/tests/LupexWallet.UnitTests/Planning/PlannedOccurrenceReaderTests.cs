using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Planning.Application;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;
using static LupexWallet.UnitTests.Planning.PlanningTestData;

namespace LupexWallet.UnitTests.Planning;

public sealed class PlannedOccurrenceReaderTests
{
    private readonly IOperationFactsLookup _facts = Substitute.For<IOperationFactsLookup>();

    private (PlannedTransaction Plan, DateOnly Date, OperationId OperationId) FullyExecutedExpense()
    {
        var date = D(2026, 1, 15);
        var plan = Monthly(date, amount: 100m);
        var operationId = Execute(plan, date, 100m);
        return (plan, date, operationId);
    }

    private void FactIs(OperationId id, CurrencyId currency, decimal appliedDelta) =>
        _facts.GetByIdsAsync(Arg.Any<IReadOnlyCollection<OperationId>>(), Arg.Any<CancellationToken>())
            .Returns([new OperationFact(id, new WalletId(Guid.NewGuid()), new OperationTypeId(Guid.NewGuid()), currency,
                100m, appliedDelta, D(2026, 1, 15), IsTransferLeg: false, IsCorrection: false, IsDeleted: false)]);

    [Fact]
    public async Task ExecutionOperation_StillAnExpenseInPlanCurrency_CountsAsExecuted()
    {
        var (plan, date, operationId) = FullyExecutedExpense();
        FactIs(operationId, Currency, -100m);

        var occurrence = await new PlannedOccurrenceReader(_facts).ProjectOneAsync(plan, date, CancellationToken.None);

        occurrence.Status.Should().Be(PlannedOccurrenceStatus.Executed);
    }

    [Fact]
    public async Task ExecutionOperation_EditedIntoIncome_NoLongerFulfilsThePlannedExpense()
    {
        var (plan, date, operationId) = FullyExecutedExpense();
        FactIs(operationId, Currency, +100m);

        var occurrence = await new PlannedOccurrenceReader(_facts).ProjectOneAsync(plan, date, CancellationToken.None);

        occurrence.Status.Should().NotBe(PlannedOccurrenceStatus.Executed);
    }

    [Fact]
    public async Task ExecutionOperation_MovedToWalletInAnotherCurrency_NoLongerFulfilsThePlan()
    {
        var (plan, date, operationId) = FullyExecutedExpense();
        FactIs(operationId, new CurrencyId(Guid.NewGuid()), -100m);

        var occurrence = await new PlannedOccurrenceReader(_facts).ProjectOneAsync(plan, date, CancellationToken.None);

        occurrence.Status.Should().NotBe(PlannedOccurrenceStatus.Executed);
    }
}
