namespace LupexWallet.SharedKernel;

/// <summary>
/// Небольшой не-generic маркер, чтобы EF Core SaveChanges-перехватчик в
/// BuildingBlocks.Infrastructure мог найти агрегаты с непубликованными событиями
/// в ChangeTracker без знания конкретного TId каждого агрегата.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

/// <summary>
/// Базовый класс для сущности-корня агрегата (ddd-model.md, "Общие соглашения":
/// мутация только через корень агрегата). Собирает доменные события до момента,
/// когда BuildingBlocks.Infrastructure опубликует их в рамках транзакции команды.
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
