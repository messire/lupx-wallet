using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LupexWallet.Operations.Api;

/// <summary>
/// Single mapping of Operations errors to ProblemDetails (docs/api/api-design.md, «Ошибки вложенных команд»).
/// Used by the Operations endpoints and by the Host for an Operations error raised inside another module's
/// command (an operation recorded for a debt payment or a plan execution).
/// </summary>
public static class OperationsProblemDetails
{
    public const string ValidationType = "https://lupexwallet/errors/operation-validation-error";
    public const string ConflictType = "https://lupexwallet/errors/operation-conflict";

    /// <summary>
    /// Soft-delete lifecycle violations (ADR-0002, §3) and concurrent modification — all answered
    /// with 409. TransferLegMismatchException is deliberately absent: it signals corrupted data (500).
    /// </summary>
    public static bool IsLifecycleConflict(OperationsDomainException ex) => ex is
        OperationPartOfTransferException or OperationAlreadyDeletedException or OperationNotDeletedException
        or OperationIsDeletedException or TransferAlreadyDeletedException or TransferNotDeletedException
        or CorrectionNotEditableException or RecordModifiedConcurrentlyException;

    /// <summary>
    /// Default mapping: lifecycle conflict → 409, any other Operations domain error → 400; null for other
    /// exceptions and for TransferLegMismatchException (corrupted data stays a 500).
    /// </summary>
    public static ProblemDetails? TryCreate(Exception exception, string? instance = null) => exception switch
    {
        TransferLegMismatchException => null,
        OperationsDomainException ex when IsLifecycleConflict(ex) => Conflict(ex, instance),
        OperationsDomainException ex => Validation(ex),
        _ => null,
    };

    public static ProblemDetails Validation(OperationsDomainException ex) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Operation validation failed",
        Detail = ex.Message,
        Type = ValidationType,
    };

    public static ProblemDetails Conflict(OperationsDomainException ex, string? instance = null) => new()
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Operation conflict",
        Detail = ex.Message,
        Instance = instance,
        Type = ConflictType,
    };
}
