using LupexWallet.Funds.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LupexWallet.Funds.Api;

/// <summary>
/// Single mapping of Funds errors to ProblemDetails (docs/api/api-design.md, «Ошибки вложенных команд»).
/// Used by the Funds endpoints and by the Host for a Funds error raised inside another module's command
/// (the linked reserve released or restored by a debt payment, an operation delete or restore).
/// FundNotFoundException is not mapped: the Funds endpoints answer it with 404 themselves.
/// </summary>
public static class FundsProblemDetails
{
    public const string ValidationType = "https://lupexwallet/errors/fund-validation-error";
    public const string ConflictType = "https://lupexwallet/errors/fund-conflict";
    public const string InsufficientFreeFundsType = "https://lupexwallet/errors/fund-insufficient-free-funds";

    public static ProblemDetails? TryCreate(Exception exception) => exception switch
    {
        FundInsufficientFreeFundsException ex =>
            Create(StatusCodes.Status409Conflict, "Insufficient free funds", ex.Message, InsufficientFreeFundsType),
        FundConflictException ex => Create(StatusCodes.Status409Conflict, "Fund conflict", ex.Message, ConflictType),
        FundValidationException ex => Validation(ex.Message),
        _ => null,
    };

    public static ProblemDetails Validation(string detail) =>
        Create(StatusCodes.Status400BadRequest, "Fund validation failed", detail, ValidationType);

    private static ProblemDetails Create(int status, string title, string detail, string type) =>
        new() { Status = status, Title = title, Detail = detail, Type = type };
}
