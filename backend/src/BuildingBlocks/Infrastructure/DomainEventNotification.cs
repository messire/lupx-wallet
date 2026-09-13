using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Wraps a domain event (SharedKernel.IDomainEvent, with no dependency on MediatR) in a
/// MediatR.INotification for publishing to subscribers in other modules
/// (high-level-architecture.md, §4).
/// </summary>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;
