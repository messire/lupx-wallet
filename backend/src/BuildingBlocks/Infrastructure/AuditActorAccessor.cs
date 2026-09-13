namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Кто выполняет текущую цепочку обработки — пользователь по HTTP или фоновый процесс
/// (ADR-0010). Живёт в BuildingBlocks.Infrastructure, а не в Audit.Application/Domain:
/// читают/пишут его не только обработчики Audit, но и фоновые сервисы других модулей
/// (BalanceHistory.Infrastructure.BalanceSnapshotSchedulerHostedService,
/// ExchangeRates.Infrastructure.ExchangeRateRefreshBackgroundService) — размещение внутри
/// модуля Audit потребовало бы от них прямой ссылки на Audit, запрещённой правилом границы
/// модуля (high-level-architecture.md, §2).
/// </summary>
public sealed record AuditActorContext(bool IsSystem, string? SystemProcessName)
{
    public static readonly AuditActorContext User = new(IsSystem: false, SystemProcessName: null);
}

public interface IAuditActorAccessor
{
    AuditActorContext Current { get; }

    /// <summary>
    /// Вызывается фоновыми сервисами первым действием внутри собственного DI-scope, до
    /// отправки MediatR-команды — иначе аудит по умолчанию пометил бы системное действие
    /// как выполненное пользователем (см. <see cref="AuditActorContext.User"/>).
    /// </summary>
    void SetSystemActor(string processName);
}

/// <summary>
/// Scoped: ASP.NET Core создаёт новый DI-scope на каждый HTTP-запрос, поэтому свежий
/// экземпляр уже по умолчанию "User" — отдельный middleware не требуется. Каскадные
/// побочные эффекты, вызванные синхронно внутри того же scope (например, пересчёт истории
/// баланса из обработчика события Operations), корректно наследуют то же значение.
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
