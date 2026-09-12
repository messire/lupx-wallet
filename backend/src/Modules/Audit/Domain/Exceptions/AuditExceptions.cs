namespace LupexWallet.Audit.Domain;

public abstract class AuditDomainException(string message) : Exception(message);

public sealed class AuditEntityTypeRequiredException() : AuditDomainException("EntityType записи аудита обязателен.");

public sealed class AuditActionRequiredException() : AuditDomainException("Action записи аудита обязателен.");

public sealed class AuditSystemProcessNameRequiredException()
    : AuditDomainException("SystemProcessName обязателен для актора System (AuditActor.System).");
