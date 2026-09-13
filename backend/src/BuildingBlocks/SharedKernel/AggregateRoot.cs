namespace LupexWallet.SharedKernel;

/// <summary>
/// Small non-generic marker so the EF Core SaveChanges interceptor in
/// BuildingBlocks.Infrastructure can find aggregates with unpublished events in the
/// ChangeTracker without knowing each aggregate's concrete TId.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// Base class for an aggregate root entity (ddd-model.md, "General conventions": mutation
/// only through the aggregate root). Collects domain events until BuildingBlocks.Infrastructure
/// publishes them within the command's transaction.
/// </summary>
public abstract class AggregateRoot<TId> : IHasDomainEvents where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected set; } = default!;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    public override bool Equals(object? obj) =>
        obj is AggregateRoot<TId> other && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override int GetHashCode() => EqualityComparer<TId>.Default.GetHashCode(Id!);
}
