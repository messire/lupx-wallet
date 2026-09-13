using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>Reference data events (ddd-model.md, §6, "ReferenceData" section).</summary>
public sealed record WalletTypeCreated(WalletTypeId WalletTypeId) : DomainEvent;
public sealed record WalletTypeDeactivated(WalletTypeId WalletTypeId) : DomainEvent;
public sealed record WalletTypeDeleted(WalletTypeId WalletTypeId) : DomainEvent;

public sealed record OperationTypeCreated(OperationTypeId OperationTypeId) : DomainEvent;
public sealed record OperationTypeDeactivated(OperationTypeId OperationTypeId) : DomainEvent;
public sealed record OperationTypeDeleted(OperationTypeId OperationTypeId) : DomainEvent;

public sealed record CurrencyCreated(CurrencyId CurrencyId) : DomainEvent;
public sealed record CurrencyDeactivated(CurrencyId CurrencyId) : DomainEvent;
public sealed record CurrencyDeleted(CurrencyId CurrencyId) : DomainEvent;
