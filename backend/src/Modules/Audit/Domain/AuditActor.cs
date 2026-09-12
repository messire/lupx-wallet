namespace LupexWallet.Audit.Domain;

public enum AuditActorKind
{
    User,
    System,
}

/// <summary>
/// VO of the change initiator (ddd-model.md, §4). SystemProcessName is set only for
/// Kind = System (name of the background service — BalanceSnapshotSchedulerHostedService,
/// ExchangeRateRefreshBackgroundService).
/// </summary>
public sealed record AuditActor
{
    public AuditActorKind Kind { get; }

    public string? SystemProcessName { get; }

    private AuditActor(AuditActorKind kind, string? systemProcessName)
    {
        Kind = kind;
        SystemProcessName = systemProcessName;
    }

    public static AuditActor User() => new(AuditActorKind.User, null);

    public static AuditActor System(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new AuditSystemProcessNameRequiredException();
        }

        return new AuditActor(AuditActorKind.System, processName);
    }
}
