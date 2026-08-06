using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем ReferenceData для модуля Operations
/// (ADR-0007) — синхронное разрешение поведения типа операции, обязательное для
/// корректности команд создания операции/перевода.
/// </summary>
public interface IOperationTypeLookup
{
    Task<OperationTypeLookupResult?> GetAsync(OperationTypeId id, CancellationToken cancellationToken);

    /// <summary>
    /// Первый активный тип операции с заданным поведением — принимает код поведения
    /// (например, "Transfer"), а не Id, чтобы вызывающий модуль (Operations) не должен
    /// был знать конкретный Guid системного справочника; коды поведений — часть
    /// стабильного словаря, зафиксированного в разделе 3 требований, а не деталь
    /// реализации ReferenceData. Используется при создании перевода — см.
    /// Operations.Application, CreateTransferCommand. Раздел 4 требований не описывает
    /// перевод как имеющий пользовательский под-тип, поэтому перевод использует единый
    /// существующий тип с поведением Transfer, а не запрашивает его явно у вызывающего
    /// (контракт /transfers не меняется).
    /// </summary>
    Task<OperationTypeLookupResult?> FindActiveByBehaviorKindCodeAsync(string behaviorKindCode, CancellationToken cancellationToken);
}

public sealed record OperationTypeLookupResult(OperationTypeId Id, string Name, OperationBehaviorKindId BehaviorKindId, string BehaviorKindCode, bool IsActive);
