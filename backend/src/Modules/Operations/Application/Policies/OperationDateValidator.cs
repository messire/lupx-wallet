using LupexWallet.Operations.Domain;

namespace LupexWallet.Operations.Application;

/// <summary>See OperationDateOutOfRangeException for the rationale behind the bounds.</summary>
public static class OperationDateValidator
{
    public static void EnsureInRange(DateOnly operationDate, DateOnly accountingStartDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (operationDate < accountingStartDate || operationDate > today)
        {
            throw new OperationDateOutOfRangeException(operationDate, accountingStartDate, today);
        }
    }

    /// <summary>The same bounds for the date of a stated balance, with a message about the balance date.</summary>
    public static void EnsureStatedBalanceDateInRange(DateOnly date, DateOnly accountingStartDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date < accountingStartDate || date > today)
        {
            throw new StatedBalanceDateOutOfRangeException(date, accountingStartDate, today);
        }
    }
}
