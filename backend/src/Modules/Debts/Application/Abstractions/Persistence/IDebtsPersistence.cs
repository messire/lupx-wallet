using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Application;

public interface ICreditorRepository
{
    void Add(Creditor creditor);
    void Remove(Creditor creditor);
    Task<Creditor?> GetByIdAsync(CreditorId id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Creditor>> GetByIdsAsync(IReadOnlyCollection<CreditorId> ids, CancellationToken cancellationToken);

    /// <summary>Ordered by (Name, CreatedAt).</summary>
    Task<PageResult<Creditor>> ListAsync(bool includeInactive, CreditorCursor? afterCursor, int limit, CancellationToken cancellationToken);

    /// <summary>Referenced by any debt or loan, cancelled ones included.</summary>
    Task<bool> IsUsedAsync(CreditorId id, CancellationToken cancellationToken);
}

public interface IDebtRepository
{
    void Add(Debt debt);
    void Remove(Debt debt);
    Task<Debt?> GetByIdAsync(DebtId id, CancellationToken cancellationToken);

    /// <summary>Locks the row until the transaction ends (SELECT ... FOR UPDATE), then loads the aggregate.</summary>
    Task<Debt?> GetForUpdateAsync(DebtId id, CancellationToken cancellationToken);

    /// <summary>Ordered by CreatedAt.</summary>
    Task<PageResult<Debt>> ListAsync(CreditorId? creditorId, DateTimeOffset? afterCreatedAt, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<Debt>> ListNotCancelledAsync(CancellationToken cancellationToken);
}

public interface ILoanRepository
{
    void Add(Loan loan);
    void Remove(Loan loan);
    Task<Loan?> GetByIdAsync(LoanId id, CancellationToken cancellationToken);

    /// <summary>Locks the row until the transaction ends (SELECT ... FOR UPDATE), then loads the aggregate.</summary>
    Task<Loan?> GetForUpdateAsync(LoanId id, CancellationToken cancellationToken);

    /// <summary>Ordered by CreatedAt.</summary>
    Task<PageResult<Loan>> ListAsync(CreditorId? creditorId, DateTimeOffset? afterCreatedAt, int limit, CancellationToken cancellationToken);
}

public interface IDebtsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PageResult<T>(IReadOnlyList<T> Items, bool HasMore);
