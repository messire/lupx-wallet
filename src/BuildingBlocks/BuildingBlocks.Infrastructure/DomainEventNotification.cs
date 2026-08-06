using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Оборачивает доменное событие (SharedKernel.IDomainEvent, без зависимости от MediatR)
/// в MediatR.INotification для публикации подписчикам из других модулей
/// (high-level-architecture.md, §4).
/// </summary>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;
