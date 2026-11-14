using FluentAssertions;
using LupexWallet.Budgeting.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Budgeting;

public sealed class ExpenseCategoryTests
{
    [Fact]
    public void Create_TrimsName_DeduplicatesTypes_RaisesCreated()
    {
        var type = OperationTypeId.New();

        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "  Еда ", [type, type]);

        category.Name.Should().Be("Еда");
        category.IsActive.Should().BeTrue();
        category.OperationTypeIds.Should().Equal(type);
        category.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExpenseCategoryCreated>()
            .Which.OperationTypeIds.Should().Equal(type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Throws(string name) =>
        FluentActions.Invoking(() => ExpenseCategory.Create(ExpenseCategoryId.New(), name, []))
            .Should().Throw<ExpenseCategoryNameRequiredException>();

    [Fact]
    public void Create_TooLongName_Throws() =>
        FluentActions.Invoking(() => ExpenseCategory.Create(ExpenseCategoryId.New(), new string('a', 201), []))
            .Should().Throw<ExpenseCategoryNameTooLongException>();

    [Fact]
    public void Update_ReplacesMembersByDiff_AndReportsOldAndNewSets()
    {
        var keep = OperationTypeId.New();
        var drop = OperationTypeId.New();
        var add = OperationTypeId.New();
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "Еда", [keep, drop]);
        category.ClearDomainEvents();
        var keptMember = category.Members.Single(m => m.OperationTypeId == keep);

        category.Update(null, [keep, add]);

        category.OperationTypeIds.Should().BeEquivalentTo([keep, add]);
        category.Members.Should().Contain(keptMember, "an unchanged member is not re-created");
        var updated = category.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExpenseCategoryUpdated>().Subject;
        updated.OldOperationTypeIds.Should().BeEquivalentTo([keep, drop]);
        updated.NewOperationTypeIds.Should().BeEquivalentTo([keep, add]);
    }

    [Fact]
    public void Update_NothingChanged_RaisesNothing()
    {
        var type = OperationTypeId.New();
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "Еда", [type]);
        category.ClearDomainEvents();

        category.Update("Еда", [type]);

        category.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_NameOnly_RaisesUpdatedWithoutMemberSets()
    {
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "Еда", []);
        category.ClearDomainEvents();

        category.Update("Продукты", null);

        category.Name.Should().Be("Продукты");
        var updated = category.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExpenseCategoryUpdated>().Subject;
        updated.OldOperationTypeIds.Should().BeNull();
    }

    [Fact]
    public void Deactivate_IsIdempotent()
    {
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "Еда", []);
        category.ClearDomainEvents();

        category.Deactivate();
        category.Deactivate();

        category.IsActive.Should().BeFalse();
        category.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ExpenseCategoryDeactivated>();
    }

    [Fact]
    public void EnsureCanBeDeleted_UsedByBudget_Throws()
    {
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), "Еда", []);

        FluentActions.Invoking(() => category.EnsureCanBeDeleted(true)).Should().Throw<ExpenseCategoryInUseException>()
            .WithMessage("*«Еда»*деактивацию*");
        FluentActions.Invoking(() => category.EnsureCanBeDeleted(false)).Should().NotThrow();
    }
}

public sealed class BudgetTests
{
    private static Budget NewBudget() => Budget.Create(BudgetId.New(), new BudgetMonth(2026, 10), CurrencyId.New());

    [Fact]
    public void SetLimit_AddsThenChanges_RaisingOldAndNewValues()
    {
        var budget = NewBudget();
        budget.ClearDomainEvents();
        var category = ExpenseCategoryId.New();

        budget.SetLimit(category, 100m);
        budget.SetLimit(category, 120.5m);
        budget.SetLimit(category, 120.5m);

        budget.Limits.Should().ContainSingle().Which.Limit.Should().Be(120.5m);
        budget.DomainEvents.OfType<BudgetCategoryLimitSet>().Select(e => (e.OldLimit, e.NewLimit))
            .Should().Equal((null, 100m), (100m, 120.5m));
    }

    [Fact]
    public void SetLimit_Negative_Throws_ZeroIsAllowed()
    {
        var budget = NewBudget();

        FluentActions.Invoking(() => budget.SetLimit(ExpenseCategoryId.New(), -0.01m)).Should().Throw<BudgetLimitInvalidException>();
        FluentActions.Invoking(() => budget.SetLimit(ExpenseCategoryId.New(), 0m)).Should().NotThrow();
    }

    [Fact]
    public void RemoveLimit_ReturnsFalseWhenAbsent()
    {
        var budget = NewBudget();
        var category = ExpenseCategoryId.New();
        budget.SetLimit(category, 5m);
        budget.ClearDomainEvents();

        budget.RemoveLimit(ExpenseCategoryId.New()).Should().BeFalse();
        budget.RemoveLimit(category).Should().BeTrue();

        budget.Limits.Should().BeEmpty();
        budget.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BudgetCategoryLimitRemoved>().Which.OldLimit.Should().Be(5m);
    }

    [Fact]
    public void MarkAsDeleted_CarriesLimitsSnapshot()
    {
        var budget = NewBudget();
        var category = ExpenseCategoryId.New();
        budget.SetLimit(category, 7m);
        budget.ClearDomainEvents();

        budget.MarkAsDeleted();

        budget.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BudgetDeleted>()
            .Which.Limits.Should().Equal(new CategoryLimitSnapshot(category, 7m));
    }

    [Theory]
    [InlineData("2026-02", 2026, 2, "2026-02-28")]
    [InlineData("2024-02", 2024, 2, "2024-02-29")]
    [InlineData("2026-12", 2026, 12, "2026-12-31")]
    public void BudgetMonth_Parse_ComputesBounds(string text, int year, int month, string lastDay)
    {
        var parsed = BudgetMonth.Parse(text);

        parsed.Should().Be(new BudgetMonth(year, month));
        parsed.FirstDay.Should().Be(new DateOnly(year, month, 1));
        parsed.LastDay.Should().Be(DateOnly.Parse(lastDay));
        parsed.ToString().Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("2026-01-01")]
    [InlineData("abcd-ef")]
    public void BudgetMonth_Parse_Invalid_Throws(string? text) =>
        FluentActions.Invoking(() => BudgetMonth.Parse(text)).Should().Throw<BudgetMonthInvalidException>();
}
