using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Narrow read-only contract published by ReferenceData for Operations (ADR-0007) —
/// synchronous resolution of an operation type's behavior, required for the correctness of
/// operation/transfer creation commands.
/// </summary>
public interface IOperationTypeLookup
{
    Task<OperationTypeLookupResult?> GetAsync(OperationTypeId id, CancellationToken cancellationToken);

    /// <summary>
    /// First active operation type with the given behavior — takes a behavior code (e.g.
    /// "Transfer") rather than an Id, so the calling module (Operations) does not need to
    /// know the system reference's concrete Guid; behavior codes are part of the stable
    /// dictionary fixed in requirements §3, not a ReferenceData implementation detail. Used
    /// when creating a transfer — see Operations.Application, CreateTransferCommand.
    /// Requirements §4 does not describe a transfer as having a user-chosen sub-type, so a
    /// transfer uses the single existing Transfer-behavior type rather than requesting it
    /// explicitly from the caller (the /transfers contract is unchanged).
    /// </summary>
    Task<OperationTypeLookupResult?> FindActiveByBehaviorKindCodeAsync(string behaviorKindCode, CancellationToken cancellationToken);
}

public sealed record OperationTypeLookupResult(OperationTypeId Id, string Name, OperationBehaviorKindId BehaviorKindId, string BehaviorKindCode, bool IsActive);
