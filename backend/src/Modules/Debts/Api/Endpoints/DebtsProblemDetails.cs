using LupexWallet.Debts.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LupexWallet.Debts.Api;

/// <summary>
/// Single mapping of Debts errors to ProblemDetails (docs/api/api-design.md, «Ошибки вложенных команд»):
/// 400 invalid input/reference, 409 state conflict. Used by the Debts endpoints and by the Host.
/// </summary>
public static class DebtsProblemDetails
{
    public const string ValidationType = "https://lupexwallet/errors/debts-validation-error";
    public const string ConflictType = "https://lupexwallet/errors/debts-conflict";

    public static ProblemDetails? TryCreate(Exception exception, string? instance = null) => exception switch
    {
        DebtsValidationException ex => Validation(ex.Message),
        DebtsConflictException ex => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Debts conflict",
            Detail = ex.Message,
            Instance = instance,
            Type = ConflictType,
        },
        _ => null,
    };

    public static ProblemDetails Validation(string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Debts validation failed",
        Detail = detail,
        Type = ValidationType,
    };
}
