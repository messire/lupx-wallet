using LupexWallet.Planning.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LupexWallet.Planning.Api;

/// <summary>
/// Single mapping of Planning errors to ProblemDetails (docs/api/api-design.md, «Ошибки вложенных команд»):
/// PlanningConflictException → 409, other PlanningDomainException → 400. PlanningNotFoundException is not
/// mapped here: the Planning endpoints answer it with 404 themselves. Used by the Planning endpoints and the Host.
/// </summary>
public static class PlanningProblemDetails
{
    public const string ValidationType = "https://lupexwallet/errors/planning-validation-error";
    public const string ConflictType = "https://lupexwallet/errors/planning-conflict";

    public static ProblemDetails? TryCreate(Exception exception) => exception switch
    {
        PlanningNotFoundException => null,
        PlanningConflictException ex => new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict, Title = "Planning conflict", Detail = ex.Message, Type = ConflictType,
        },
        PlanningDomainException ex => new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest, Title = "Planning validation failed", Detail = ex.Message, Type = ValidationType,
        },
        _ => null,
    };
}
