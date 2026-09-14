namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Identifies who is executing the current processing chain — an HTTP user or a background
/// process (ADR-0010). Lives in BuildingBlocks.Infrastructure rather than
/// Audit.Application/Domain because it is read/written not only by Audit handlers but also
/// by background services in other modules
/// (BalanceHistory.Infrastructure.BalanceSnapshotSchedulerHostedService,
/// ExchangeRates.Infrastructure.ExchangeRateRefreshBackgroundService) — placing it inside
/// the Audit module would require them to reference Audit directly, which the module
/// boundary rule forbids (high-level-architecture.md, §2).
/// </summary>
public sealed record AuditActorContext(bool IsSystem, string? SystemProcessName)
{
    public static readonly AuditActorContext User = new(IsSystem: false, SystemProcessName: null);
}

public interface IAuditActorAccessor
{
    AuditActorContext Current { get; }

    /// <summary>
    /// Must be called by background services as the first action within their own DI
    /// scope, before sending a MediatR command — otherwise audit would default to marking
    /// a system action as performed by a user (see <see cref="AuditActorContext.User"/>).
    /// </summary>
    void SetSystemActor(string processName);
}

/// <summary>
/// Scoped: ASP.NET Core creates a new DI scope per HTTP request, so a fresh instance
/// already defaults to "User" — no separate middleware is needed. Cascading side effects
/// triggered synchronously within the same scope (e.g. balance history recalculation from
/// an Operations event handler) correctly inherit the same value.
/// </summary>
public sealed class AuditActorAccessor : IAuditActorAccessor
{
    public AuditActorContext Current { get; private set; } = AuditActorContext.User;

    public void SetSystemActor(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new ArgumentException("Имя системного процесса обязательно.", nameof(processName));
        }

        Current = new AuditActorContext(IsSystem: true, SystemProcessName: processName);
    }
}
