using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Domain;

/// <summary>
/// Агрегат BalanceSnapshot (ddd-model.md, §2.6) — идентифицируется парой
/// (WalletId, SnapshotDate), а не отдельным суррогатным Id (docs/database/schema.md:
/// составной первичный ключ balance_snapshots — явное требование раздела 5 "не более
/// одной записи на кошелек и дату"). Поэтому, в отличие от остальных агрегатов проекта,
/// не наследует AggregateRoot&lt;TId&gt; (рассчитан на единственный Guid-обертку Id) —
/// реализует IHasDomainEvents напрямую с двумя полями идентичности, по аналогии с тем,
/// как Operation хранит WalletId/OperationDate как обычные свойства.
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
    /// Пересчитывает значение слепка (каскадный пересчет, ADR-0003, или плановая/
    /// досоздающая задача, ADR-0004) — вызывающий код (BalanceRecalculationService) не
    /// вызывает этот метод, если новое значение совпадает с текущим, чтобы не порождать
    /// лишние записи аудита при отсутствии фактического изменения.
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
