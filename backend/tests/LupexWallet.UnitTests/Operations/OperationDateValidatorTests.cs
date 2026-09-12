using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// OperationDateValidator — границы [AccountingStartDate .. сегодня] (регресс:
/// docs/PROGRESS.md, "Баги, найденные и исправленные" — операция вне диапазона меняла
/// CurrentBalance, но не отражалась в истории баланса).
/// </summary>
public sealed class OperationDateValidatorTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void EnsureInRange_DateBeforeAccountingStartDate_ThrowsOperationDateOutOfRangeException()
    {
        var accountingStartDate = Today;
        var operationDate = accountingStartDate.AddDays(-1);

        var act = () => OperationDateValidator.EnsureInRange(operationDate, accountingStartDate);

        act.Should().Throw<OperationDateOutOfRangeException>();
    }

    [Fact]
    public void EnsureInRange_DateAfterToday_ThrowsOperationDateOutOfRangeException()
    {
        var operationDate = Today.AddDays(1);

        var act = () => OperationDateValidator.EnsureInRange(operationDate, Today.AddDays(-30));

        act.Should().Throw<OperationDateOutOfRangeException>();
    }

    [Fact]
    public void EnsureInRange_DateEqualsAccountingStartDate_DoesNotThrow()
    {
        var accountingStartDate = Today.AddDays(-10);

        var act = () => OperationDateValidator.EnsureInRange(accountingStartDate, accountingStartDate);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureInRange_DateEqualsToday_DoesNotThrow()
    {
        var act = () => OperationDateValidator.EnsureInRange(Today, Today.AddDays(-10));

        act.Should().NotThrow();
    }
}
