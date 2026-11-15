using LupexWallet.Debts.Api;
using LupexWallet.Funds.Api;
using LupexWallet.Operations.Api;
using LupexWallet.Planning.Api;
using Microsoft.AspNetCore.Mvc;

namespace LupexWallet.Api;

/// <summary>
/// Since v0.4.0 a command of one module runs code of other modules in the same transaction (ADR-0010; the
/// payment operation of a debt, the linked reserve released by Funds subscribers, a plan execution). Each
/// endpoint maps only its own module's errors, so an error of another module raised inside the command
/// reaches this middleware and is answered with the same ProblemDetails (status, type) that module's own
/// endpoints give, instead of a 500 (docs/api/api-design.md, «Ошибки вложенных команд»). Exceptions no
/// module maps are rethrown unchanged.
/// </summary>
public static class ModuleExceptionMapping
{
    /// <summary>Explicit list of the modules' mappings; the first non-null answer wins.</summary>
    private static readonly Func<Exception, ProblemDetails?>[] Mappers =
    [
        ex => OperationsProblemDetails.TryCreate(ex),
        FundsProblemDetails.TryCreate,
        ex => DebtsProblemDetails.TryCreate(ex),
        PlanningProblemDetails.TryCreate,
    ];

    public static ProblemDetails? TryMap(Exception exception)
    {
        foreach (var mapper in Mappers)
        {
            if (mapper(exception) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    public static IApplicationBuilder UseModuleExceptionMapping(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.Response.HasStarted && TryMap(ex) is { } problem)
            {
                context.Response.Clear();
                await Results.Problem(problem).ExecuteAsync(context);
            }
        });
}
