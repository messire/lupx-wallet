using FluentAssertions;
using LupexWallet.Budgeting.Application;
using LupexWallet.Budgeting.Domain;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Budgeting;

public sealed class GetBudgetExecutionQueryHandlerTests
{
    private static readonly CurrencyId Eur = CurrencyId.New();
    private static readonly CurrencyId Usd = CurrencyId.New();
    private static readonly OperationTypeId Food = OperationTypeId.New();
    private static readonly OperationTypeId Fuel = OperationTypeId.New();
    private static readonly OperationTypeId Salary = OperationTypeId.New();
    private static readonly DateOnly Day1 = new(2026, 10, 1);

    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly IExpenseCategoryRepository _categories = Substitute.For<IExpenseCategoryRepository>();
    private readonly IOperationFactsLookup _facts = Substitute.For<IOperationFactsLookup>();
    private readonly IOperationTypeLookup _types = Substitute.For<IOperationTypeLookup>();
    private readonly IExchangeRateLookup _rates = Substitute.For<IExchangeRateLookup>();
    private readonly ICurrencyLookup _currencies = Substitute.For<ICurrencyLookup>();

    public GetBudgetExecutionQueryHandlerTests()
    {
        _types.GetAsync(Food, Arg.Any<CancellationToken>()).Returns(Type(Food, "Expense"));
        _types.GetAsync(Fuel, Arg.Any<CancellationToken>()).Returns(Type(Fuel, "Expense"));
        _types.GetAsync(Salary, Arg.Any<CancellationToken>()).Returns(Type(Salary, "Income"));
        _currencies.GetAsync(Eur, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Eur, "EUR"));
        _currencies.GetAsync(Usd, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Usd, "USD"));
    }

    private static OperationTypeLookupResult Type(OperationTypeId id, string code) =>
        new(id, code + "-type", OperationBehaviorKindId.New(), code, true);

    private static OperationFact Fact(OperationTypeId? type, CurrencyId currency, decimal amount, DateOnly date) =>
        new(OperationId.New(), WalletId.New(), type, currency, amount, -amount, date, IsTransferLeg: false, IsCorrection: false, IsDeleted: false);

    private GetBudgetExecutionQueryHandler Handler() => new(_budgets, _categories, _facts, _types, _rates, _currencies);

    private (Budget Budget, ExpenseCategory Eating) Arrange(CurrencyId budgetCurrency, params OperationFact[] facts)
    {
        var budget = Budget.Create(BudgetId.New(), new BudgetMonth(2026, 10), budgetCurrency);
        var eating = ExpenseCategory.Create(ExpenseCategoryId.New(), "Питание", [Food]);
        var car = ExpenseCategory.Create(ExpenseCategoryId.New(), "Машина", [Fuel]); // no limit
        budget.SetLimit(eating.Id, 100m);
        _budgets.GetByIdAsync(budget.Id, Arg.Any<CancellationToken>()).Returns(budget);
        _categories.GetAllAsync(Arg.Any<CancellationToken>()).Returns([eating, car]);
        _facts.ListInRangeAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Arg.Any<CancellationToken>()).Returns(facts);
        return (budget, eating);
    }

    [Fact]
    public async Task UnknownBudget_ReturnsNull()
    {
        (await Handler().Handle(new GetBudgetExecutionQuery(Guid.NewGuid()), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task SameCurrency_SplitsLimitedAndUnbudgeted_IgnoresIncomeAndUntyped()
    {
        var (budget, eating) = Arrange(Eur,
            Fact(Food, Eur, 60m, Day1),
            Fact(Food, Eur, 50m, Day1.AddDays(30)),
            Fact(Fuel, Eur, 20m, Day1),
            Fact(Salary, Eur, 1000m, Day1),
            Fact(null, Eur, 5m, Day1));

        var result = (await Handler().Handle(new GetBudgetExecutionQuery(budget.Id.Value), CancellationToken.None))!;

        var row = result.Categories.Should().ContainSingle().Subject;
        row.Should().Be(new CategoryExecutionDto(eating.Id.Value, "Питание", true, 100m, 110m, -10m, true));
        result.UnbudgetedActual.Should().Be(20m);
        (result.TotalLimit, result.TotalBudgetedActual, result.TotalRemaining, result.TotalActual).Should().Be((100m, 110m, -10m, 130m));
        result.Month.Should().Be("2026-10");
        await _rates.DidNotReceiveWithAnyArgs().GetHistoricalRateAsync(default, default, default, default);
    }

    [Fact]
    public async Task OtherCurrency_DirectRate_FallbackFlag_CachedPerCurrencyAndDate()
    {
        var saturday = new DateOnly(2026, 10, 3);
        var friday = new DateOnly(2026, 10, 2);
        var (budget, _) = Arrange(Eur, Fact(Food, Usd, 10m, saturday), Fact(Food, Usd, 30m, saturday));
        _rates.GetHistoricalRateAsync(Usd, Eur, saturday, Arg.Any<CancellationToken>()).Returns((0.5m, friday));

        var result = (await Handler().Handle(new GetBudgetExecutionQuery(budget.Id.Value), CancellationToken.None))!;

        result.Categories.Single().Actual.Should().Be(20m);
        result.AppliedRates.Should().Equal(new AppliedRateDto(Usd.Value, saturday, friday, 0.5m, IsFallback: true));
        await _rates.Received(1).GetHistoricalRateAsync(Usd, Eur, saturday, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherCurrency_OnlyInversePair_UsesReciprocal()
    {
        var (budget, _) = Arrange(Usd, Fact(Food, Eur, 10m, Day1));
        _rates.GetHistoricalRateAsync(Eur, Usd, Day1, Arg.Any<CancellationToken>()).Returns(((decimal, DateOnly)?)null);
        _rates.GetHistoricalRateAsync(Usd, Eur, Day1, Arg.Any<CancellationToken>()).Returns((0.5m, Day1));

        var result = (await Handler().Handle(new GetBudgetExecutionQuery(budget.Id.Value), CancellationToken.None))!;

        result.Categories.Single().Actual.Should().Be(20m);
        result.AppliedRates.Should().Equal(new AppliedRateDto(Eur.Value, Day1, Day1, 2m, IsFallback: false));
    }

    [Fact]
    public async Task NoRate_ExcludesOperationWithReason()
    {
        var fact = Fact(Food, Usd, 10m, Day1);
        var (budget, _) = Arrange(Eur, fact, Fact(Food, Eur, 1m, Day1));
        _rates.GetHistoricalRateAsync(default, default, default, default).ReturnsForAnyArgs(((decimal, DateOnly)?)null);

        var result = (await Handler().Handle(new GetBudgetExecutionQuery(budget.Id.Value), CancellationToken.None))!;

        result.Categories.Single().Actual.Should().Be(1m);
        result.ExcludedOperations.Should().Equal(new ExcludedOperationDto(
            fact.OperationId.Value, Usd.Value, Day1, 10m, "Курс валюты USD к EUR на дату 2026-10-01 недоступен."));
        result.AppliedRates.Should().BeEmpty();
    }
}

public sealed class ExpenseCategoryMembershipPolicyTests
{
    private readonly IOperationTypeLookup _types = Substitute.For<IOperationTypeLookup>();
    private readonly IExpenseCategoryRepository _categories = Substitute.For<IExpenseCategoryRepository>();

    private OperationTypeId AddType(string code, string name, bool isActive = true)
    {
        var id = OperationTypeId.New();
        _types.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new OperationTypeLookupResult(id, name, OperationBehaviorKindId.New(), code, isActive));
        return id;
    }

    private ExpenseCategoryMembershipPolicy Policy(params ExpenseCategory[] existing)
    {
        _categories.GetAllAsync(Arg.Any<CancellationToken>()).Returns(existing);
        return new ExpenseCategoryMembershipPolicy(_types, _categories);
    }

    [Fact]
    public async Task Rejects_UnknownType_NonExpenseType_TypeOfAnotherCategory()
    {
        var food = AddType("Expense", "Еда");
        var salary = AddType("Income", "Зарплата");
        var other = ExpenseCategory.Create(ExpenseCategoryId.New(), "Питание", [food]);
        var policy = Policy(other);

        await FluentActions.Awaiting(() => policy.ValidateAsync(null, [Guid.NewGuid()], CancellationToken.None))
            .Should().ThrowAsync<OperationTypeNotFoundForCategoryException>();
        await FluentActions.Awaiting(() => policy.ValidateAsync(null, [salary.Value], CancellationToken.None))
            .Should().ThrowAsync<OperationTypeNotExpenseException>();
        await FluentActions.Awaiting(() => policy.ValidateAsync(null, [food.Value], CancellationToken.None))
            .Should().ThrowAsync<OperationTypeAlreadyInCategoryException>().WithMessage("*«Еда»*«Питание»*");
    }

    [Fact]
    public async Task OwnMembers_AreNotAConflict_AndAKeptInactiveTypeIsAllowedButNotANewOne()
    {
        var food = AddType("Expense", "Еда");
        var oldType = AddType("Expense", "Старое", isActive: false);
        var newInactive = AddType("Expense", "Новое неактивное", isActive: false);
        var self = ExpenseCategory.Create(ExpenseCategoryId.New(), "Питание", [food, oldType]);
        var policy = Policy(self);

        (await policy.ValidateAsync(self, [food.Value, oldType.Value, food.Value], CancellationToken.None))
            .Should().Equal(food, oldType);
        await FluentActions.Awaiting(() => policy.ValidateAsync(self, [newInactive.Value], CancellationToken.None))
            .Should().ThrowAsync<OperationTypeInactiveForCategoryException>();
    }
}
