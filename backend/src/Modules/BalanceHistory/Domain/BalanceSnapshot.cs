using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Domain;

/// <summary>
/// BalanceSnapshot aggregate (ddd-model.md, §2.6) — identified by the pair
/// (WalletId, SnapshotDate), not a separate surrogate Id (docs/database/schema.md:
/// balance_snapshots has a composite primary key — an explicit requirement of §5, "no more
/// than one record per wallet and date"). Unlike the other aggregates in the project it
/// therefore does not inherit AggregateRoot&lt;TId&gt; (built for a single Guid-wrapper Id) —
/// it implements IHasDomainEvents directly with two identity fields, the same way Operation
/// stores WalletId/OperationDate as plain properties.
/// </summary>
public sealed class BalanceSnapshot : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private decimal _balanceAmount;

    public WalletId WalletId { get; private set; }
    public DateOnly SnapshotDate { get; private set; }
    public CurrencyId CurrencyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Money Balance => new(_balanceAmount, CurrencyId);

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    private BalanceSnapshot()
    {
        // Только для EF Core.
    }

    public static BalanceSnapshot Create(WalletId walletId, DateOnly snapshotDate, Money balance)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new BalanceSnapshot
        {
            WalletId = walletId,
            SnapshotDate = snapshotDate,
            CurrencyId = balance.CurrencyId,
            _balanceAmount = balance.Amount,
            CreatedAt = now,
            UpdatedAt = now,
        };

        snapshot.Raise(new BalanceSnapshotCreated(walletId, snapshotDate, balance));
        return snapshot;
    }

    /// <summary>
    /// Recalculates the snapshot value (cascading recalculation, ADR-0003, or the scheduled/
    /// backfill job, ADR-0004) — the caller (BalanceRecalculationService) does not call this
    /// method when the new value matches the current one, to avoid extra audit entries when
    /// nothing actually changed.
    /// </summary>
    public void UpdateBalance(Money newBalance)
    {
        if (newBalance.CurrencyId != CurrencyId)
        {
            throw new InvalidOperationException(
                $"Нельзя изменить валюту существующего слепка баланса ({CurrencyId} → {newBalance.CurrencyId}).");
        }

        var oldBalance = Balance;
        _balanceAmount = newBalance.Amount;
        UpdatedAt = DateTimeOffset.UtcNow;

        Raise(new BalanceSnapshotUpdated(WalletId, SnapshotDate, oldBalance, newBalance));
    }

    private void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
