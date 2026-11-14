using LupexWallet.Debts.Application;
using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Debts.Infrastructure;

/// <summary>Implementation of the Debts-owned read port <see cref="IDebtObligationsLookup"/> (ADR-0010 §4, §7).</summary>
public sealed class DebtObligationsLookup(DebtsDbContext dbContext, PaymentFactsReader factsReader) : IDebtObligationsLookup
{
    public async Task<ObligationReference?> FindDebtAsync(Guid debtId, CancellationToken cancellationToken)
    {
        var id = new DebtId(debtId);
        var row = await dbContext.Debts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.CurrencyId, x.CancelledAt })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new ObligationReference(ObligationKind.Debt, debtId, row.CurrencyId, row.CancelledAt != null);
    }

    public async Task<ObligationReference?> FindLoanAsync(Guid loanId, CancellationToken cancellationToken)
    {
        var id = new LoanId(loanId);
        var row = await dbContext.Loans.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.CurrencyId, x.CancelledAt })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new ObligationReference(ObligationKind.Loan, loanId, row.CurrencyId, row.CancelledAt != null);
    }

    public async Task<LinkedObligationPayment?> FindPaymentByOperationAsync(OperationId operationId, CancellationToken cancellationToken)
    {
        var debt = await dbContext.Debts.AsNoTracking()
            .Where(d => d.Repayments.Any(r => r.OperationId == operationId))
            .FirstOrDefaultAsync(cancellationToken);
        if (debt is not null)
        {
            var repayment = debt.Repayments.First(r => r.OperationId == operationId);
            return new LinkedObligationPayment(ObligationKind.Debt, debt.Id.Value, repayment.WalletId, repayment.Amount);
        }

        var loan = await dbContext.Loans.AsNoTracking().AsSplitQuery()
            .Where(l => l.Payments.Any(p => p.OperationId == operationId))
            .FirstOrDefaultAsync(cancellationToken);
        if (loan is not null)
        {
            var payment = loan.Payments.First(p => p.OperationId == operationId);
            return new LinkedObligationPayment(ObligationKind.Loan, loan.Id.Value, payment.WalletId, payment.Amount);
        }

        return null;
    }

    public async Task<IReadOnlyList<OpenObligation>> ListOpenObligationsAsync(CancellationToken cancellationToken)
    {
        var (debts, loans, facts) = await LoadOpenAsync(cancellationToken);
        var creditorNames = await CreditorNamesAsync(
            debts.Select(d => d.CreditorId).Concat(loans.Select(l => l.CreditorId)), cancellationToken);

        var result = new List<OpenObligation>();
        foreach (var debt in debts)
        {
            var remaining = debt.RemainingAmount(facts.CountedTotal(debt.Repayments.Select(r => r.OperationId), debt.CurrencyId)).Amount;
            if (remaining > 0m)
            {
                result.Add(new OpenObligation(
                    ObligationKind.Debt, debt.Id.Value, debt.Name, debt.CreditorId.Value,
                    creditorNames.GetValueOrDefault(debt.CreditorId, string.Empty), debt.CurrencyId, remaining));
            }
        }

        foreach (var loan in loans)
        {
            var remaining = loan.RemainingAmount(facts.CountedTotal(loan.Payments.Select(p => p.OperationId), loan.CurrencyId)).Amount;
            if (remaining > 0m)
            {
                result.Add(new OpenObligation(
                    ObligationKind.Loan, loan.Id.Value, loan.Name, loan.CreditorId.Value,
                    creditorNames.GetValueOrDefault(loan.CreditorId, string.Empty), loan.CurrencyId, remaining));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<UnpaidLoanScheduleItem>> ListUnpaidScheduleItemsAsync(DateOnly to, CancellationToken cancellationToken)
    {
        var (_, loans, facts) = await LoadOpenAsync(cancellationToken, includeDebts: false);
        var creditorNames = await CreditorNamesAsync(loans.Select(l => l.CreditorId), cancellationToken);
        var today = DebtsDtoMapper.Today();

        var result = new List<UnpaidLoanScheduleItem>();
        foreach (var loan in loans)
        {
            var paid = facts.CountedTotal(loan.Payments.Select(p => p.OperationId), loan.CurrencyId);
            if (loan.Status(paid) != ObligationStatus.Active)
            {
                continue;
            }

            foreach (var item in loan.EvaluateSchedule(paid, today))
            {
                if (item.Status == LoanScheduleItemStatus.Paid || item.DueDate > to)
                {
                    continue;
                }

                result.Add(new UnpaidLoanScheduleItem(
                    loan.Id.Value, loan.Name, loan.CreditorId.Value, creditorNames.GetValueOrDefault(loan.CreditorId, string.Empty),
                    loan.CurrencyId, item.DueDate, item.Amount, item.PaidAmount, item.OutstandingAmount,
                    item.Status.ToString(), item.Status == LoanScheduleItemStatus.Overdue));
            }
        }

        return result.OrderBy(x => x.DueDate).ThenBy(x => x.LoanName).ToList();
    }

    private async Task<(List<Debt> Debts, List<Loan> Loans, PaymentFacts Facts)> LoadOpenAsync(
        CancellationToken cancellationToken, bool includeDebts = true)
    {
        var debts = includeDebts
            ? await dbContext.Debts.AsNoTracking().Where(x => x.CancelledAt == null).ToListAsync(cancellationToken)
            : [];
        var loans = await dbContext.Loans.AsNoTracking().AsSplitQuery().Where(x => x.CancelledAt == null).ToListAsync(cancellationToken);

        var facts = await factsReader.ReadAsync(
            debts.SelectMany(d => d.Repayments.Select(r => r.OperationId)).Concat(loans.SelectMany(l => l.Payments.Select(p => p.OperationId))),
            cancellationToken);
        return (debts, loans, facts);
    }

    private async Task<Dictionary<CreditorId, string>> CreditorNamesAsync(IEnumerable<CreditorId> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        return await dbContext.Creditors.AsNoTracking()
            .Where(c => distinct.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }
}
