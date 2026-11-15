using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Debts.Application;

// ---- Create (B3: the debt is created on its own, no operation) ----
public sealed record CreateDebtCommand(Guid CreditorId, string Name, Guid CurrencyId, decimal OriginalAmount, DateOnly StartDate)
    : IRequest<DebtDto>, ICommand<DebtDto>;

public sealed class CreateDebtCommandHandler(
    IDebtRepository repository, IDebtsUnitOfWork unitOfWork, DebtsReferenceValidator references)
    : IRequestHandler<CreateDebtCommand, DebtDto>
{
    public async Task<DebtDto> Handle(CreateDebtCommand request, CancellationToken cancellationToken)
    {
        var creditorId = new CreditorId(request.CreditorId);
        var currencyId = new CurrencyId(request.CurrencyId);
        await references.EnsureCreditorAsync(creditorId, currentCreditorId: null, cancellationToken);
        await references.EnsureCurrencyAsync(currencyId, cancellationToken);

        var debt = Debt.Create(DebtId.New(), creditorId, request.Name, new Money(request.OriginalAmount, currencyId), request.StartDate);
        repository.Add(debt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DebtsDtoMapper.ToDto(debt, PaymentFacts.Empty);
    }
}

// ---- Update (name, creditor, original amount, start date) ----
public sealed record UpdateDebtCommand(Guid Id, Guid CreditorId, string Name, decimal OriginalAmount, DateOnly StartDate)
    : IRequest<DebtDto>, ICommand<DebtDto>;

public sealed class UpdateDebtCommandHandler(
    IDebtRepository repository, IDebtsUnitOfWork unitOfWork, DebtsReferenceValidator references, PaymentFactsReader factsReader)
    : IRequestHandler<UpdateDebtCommand, DebtDto>
{
    public async Task<DebtDto> Handle(UpdateDebtCommand request, CancellationToken cancellationToken)
    {
        var debt = await repository.GetForUpdateAsync(new DebtId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Debt {request.Id} не найден.");

        var creditorId = new CreditorId(request.CreditorId);
        await references.EnsureCreditorAsync(creditorId, debt.CreditorId, cancellationToken);

        debt.Update(creditorId, request.Name, request.OriginalAmount, request.StartDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var facts = await factsReader.ReadAsync(debt.Repayments.Select(x => x.OperationId), cancellationToken);
        return DebtsDtoMapper.ToDto(debt, facts);
    }
}

// ---- Cancel ----
public sealed record CancelDebtCommand(Guid Id) : IRequest<DebtDto>, ICommand<DebtDto>;

public sealed class CancelDebtCommandHandler(IDebtRepository repository, IDebtsUnitOfWork unitOfWork, PaymentFactsReader factsReader)
    : IRequestHandler<CancelDebtCommand, DebtDto>
{
    public async Task<DebtDto> Handle(CancelDebtCommand request, CancellationToken cancellationToken)
    {
        var debt = await repository.GetForUpdateAsync(new DebtId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Debt {request.Id} не найден.");

        debt.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var facts = await factsReader.ReadAsync(debt.Repayments.Select(x => x.OperationId), cancellationToken);
        return DebtsDtoMapper.ToDto(debt, facts);
    }
}

// ---- Delete (ADR-0010 §8: only without repayments) ----
public sealed record DeleteDebtCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteDebtCommandHandler(IDebtRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<DeleteDebtCommand>
{
    public async Task Handle(DeleteDebtCommand request, CancellationToken cancellationToken)
    {
        var debt = await repository.GetForUpdateAsync(new DebtId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Debt {request.Id} не найден.");

        debt.EnsureCanBeDeleted();
        debt.MarkAsDeleted();
        repository.Remove(debt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- Record a repayment (B4): expense from a wallet in the debt currency, created in the same transaction ----
public sealed record RecordDebtRepaymentCommand(Guid DebtId, Guid WalletId, Guid OperationTypeId, decimal Amount, DateOnly RepaymentDate)
    : IRequest<DebtDto>, ICommand<DebtDto>;

public sealed class RecordDebtRepaymentCommandHandler(
    IDebtRepository repository,
    IDebtsUnitOfWork unitOfWork,
    PaymentFactsReader factsReader,
    ExpensePaymentRecorder paymentRecorder) : IRequestHandler<RecordDebtRepaymentCommand, DebtDto>
{
    public async Task<DebtDto> Handle(RecordDebtRepaymentCommand request, CancellationToken cancellationToken)
    {
        // Row lock: two concurrent repayments must not both pass the "amount <= remaining" check.
        var debt = await repository.GetForUpdateAsync(new DebtId(request.DebtId), cancellationToken)
            ?? throw new KeyNotFoundException($"Debt {request.DebtId} не найден.");

        var walletId = new WalletId(request.WalletId);
        var operationTypeId = new OperationTypeId(request.OperationTypeId);

        var facts = await factsReader.ReadAsync(debt.Repayments.Select(x => x.OperationId), cancellationToken);
        var counted = facts.CountedTotal(debt.Repayments.Select(x => x.OperationId), debt.CurrencyId);

        var walletCurrency = await paymentRecorder.ValidateAsync(walletId, operationTypeId, request.RepaymentDate, cancellationToken);
        debt.EnsureCanAcceptRepayment(new Money(request.Amount, walletCurrency), counted);

        var operation = await paymentRecorder.RecordAsync(walletId, operationTypeId, request.Amount, request.RepaymentDate, cancellationToken);
        debt.RecordRepayment(
            operation.OperationId, walletId, new Money(operation.Amount, operation.CurrencyId), operation.OperationDate, counted);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var updatedFacts = await factsReader.ReadAsync(debt.Repayments.Select(x => x.OperationId), cancellationToken);
        return DebtsDtoMapper.ToDto(debt, updatedFacts);
    }
}

// ---- Get ----
public sealed record GetDebtQuery(Guid Id) : IRequest<DebtDto?>, IQuery<DebtDto?>;

public sealed class GetDebtQueryHandler(IDebtRepository repository, PaymentFactsReader factsReader)
    : IRequestHandler<GetDebtQuery, DebtDto?>
{
    public async Task<DebtDto?> Handle(GetDebtQuery request, CancellationToken cancellationToken)
    {
        var debt = await repository.GetByIdAsync(new DebtId(request.Id), cancellationToken);
        if (debt is null)
        {
            return null;
        }

        var facts = await factsReader.ReadAsync(debt.Repayments.Select(x => x.OperationId), cancellationToken);
        return DebtsDtoMapper.ToDto(debt, facts);
    }
}

// ---- List ----
public sealed record ListDebtsQuery(Guid? CreditorId, string? Cursor, int Limit) : IRequest<DebtPageDto>, IQuery<DebtPageDto>;

public sealed class ListDebtsQueryHandler(IDebtRepository repository, PaymentFactsReader factsReader)
    : IRequestHandler<ListDebtsQuery, DebtPageDto>
{
    public async Task<DebtPageDto> Handle(ListDebtsQuery request, CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(
            request.CreditorId is { } c ? new CreditorId(c) : null,
            CreatedAtCursor.TryDecode(request.Cursor), CreatedAtCursor.ClampLimit(request.Limit), cancellationToken);

        var facts = await factsReader.ReadAsync(page.Items.SelectMany(d => d.Repayments.Select(r => r.OperationId)), cancellationToken);
        var nextCursor = page.HasMore && page.Items.Count > 0 ? CreatedAtCursor.Encode(page.Items[^1].CreatedAt) : null;
        return new DebtPageDto(page.Items.Select(d => DebtsDtoMapper.ToDto(d, facts)).ToList(), nextCursor, page.HasMore);
    }
}

// ---- Summary (#6): total debt and breakdown by creditor, per currency ----
public sealed record GetDebtSummaryQuery : IRequest<DebtSummaryDto>, IQuery<DebtSummaryDto>;

public sealed class GetDebtSummaryQueryHandler(
    IDebtRepository repository, ICreditorRepository creditors, PaymentFactsReader factsReader)
    : IRequestHandler<GetDebtSummaryQuery, DebtSummaryDto>
{
    public async Task<DebtSummaryDto> Handle(GetDebtSummaryQuery request, CancellationToken cancellationToken)
    {
        var debts = await repository.ListNotCancelledAsync(cancellationToken);
        var facts = await factsReader.ReadAsync(debts.SelectMany(d => d.Repayments.Select(r => r.OperationId)), cancellationToken);

        // Only debts with something left to repay contribute.
        var open = debts
            .Select(d => (Debt: d, Remaining: d.RemainingAmount(facts.CountedTotal(d.Repayments.Select(r => r.OperationId), d.CurrencyId)).Amount))
            .Where(x => x.Remaining > 0m)
            .ToList();

        var creditorNames = (await creditors.GetByIdsAsync(open.Select(x => x.Debt.CreditorId).Distinct().ToList(), cancellationToken))
            .ToDictionary(x => x.Id, x => x.Name);

        var currencies = open
            .GroupBy(x => x.Debt.CurrencyId)
            .Select(byCurrency => new DebtCurrencySummaryDto(
                byCurrency.Key.Value,
                byCurrency.Sum(x => x.Remaining),
                byCurrency.Count(),
                byCurrency
                    .GroupBy(x => x.Debt.CreditorId)
                    .Select(byCreditor => new DebtCreditorSummaryDto(
                        byCreditor.Key.Value,
                        creditorNames.GetValueOrDefault(byCreditor.Key, string.Empty),
                        byCreditor.Sum(x => x.Remaining),
                        byCreditor.Count()))
                    .OrderByDescending(x => x.RemainingAmount).ThenBy(x => x.CreditorName)
                    .ToList()))
            .OrderByDescending(x => x.TotalRemainingAmount)
            .ToList();

        return new DebtSummaryDto(currencies);
    }
}
