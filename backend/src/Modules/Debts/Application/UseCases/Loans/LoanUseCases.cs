using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Debts.Application;

// ---- Create (B3: created on its own, no operation) ----
public sealed record CreateLoanCommand(Guid CreditorId, string Name, Guid CurrencyId, decimal AmountToRepay, DateOnly StartDate)
    : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class CreateLoanCommandHandler(
    ILoanRepository repository, IDebtsUnitOfWork unitOfWork, DebtsReferenceValidator references)
    : IRequestHandler<CreateLoanCommand, LoanDto>
{
    public async Task<LoanDto> Handle(CreateLoanCommand request, CancellationToken cancellationToken)
    {
        var creditorId = new CreditorId(request.CreditorId);
        var currencyId = new CurrencyId(request.CurrencyId);
        await references.EnsureCreditorAsync(creditorId, currentCreditorId: null, cancellationToken);
        await references.EnsureCurrencyAsync(currencyId, cancellationToken);

        var loan = Loan.Create(LoanId.New(), creditorId, request.Name, new Money(request.AmountToRepay, currencyId), request.StartDate);
        repository.Add(loan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return DebtsDtoMapper.ToDto(loan, PaymentFacts.Empty, DebtsDtoMapper.Today());
    }
}

// ---- Update terms ----
public sealed record UpdateLoanCommand(Guid Id, Guid CreditorId, string Name, decimal AmountToRepay, DateOnly StartDate)
    : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class UpdateLoanCommandHandler(
    ILoanRepository repository, IDebtsUnitOfWork unitOfWork, DebtsReferenceValidator references, PaymentFactsReader factsReader)
    : IRequestHandler<UpdateLoanCommand, LoanDto>
{
    public async Task<LoanDto> Handle(UpdateLoanCommand request, CancellationToken cancellationToken)
    {
        var loan = await LoanCommands.LoadAsync(repository, request.Id, cancellationToken);
        var creditorId = new CreditorId(request.CreditorId);
        await references.EnsureCreditorAsync(creditorId, loan.CreditorId, cancellationToken);

        loan.Update(creditorId, request.Name, request.AmountToRepay, request.StartDate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- Replace the schedule (reschedule dates, change amounts) ----
public sealed record ReplaceLoanScheduleCommand(Guid Id, IReadOnlyList<LoanScheduleEntry> Items)
    : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class ReplaceLoanScheduleCommandHandler(ILoanRepository repository, IDebtsUnitOfWork unitOfWork, PaymentFactsReader factsReader)
    : IRequestHandler<ReplaceLoanScheduleCommand, LoanDto>
{
    public async Task<LoanDto> Handle(ReplaceLoanScheduleCommand request, CancellationToken cancellationToken)
    {
        var loan = await LoanCommands.LoadAsync(repository, request.Id, cancellationToken);
        loan.ReplaceSchedule(request.Items);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- Generate N equal monthly payments ----
public sealed record GenerateLoanScheduleCommand(Guid Id, DateOnly FirstPaymentDate, int NumberOfPayments)
    : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class GenerateLoanScheduleCommandHandler(ILoanRepository repository, IDebtsUnitOfWork unitOfWork, PaymentFactsReader factsReader)
    : IRequestHandler<GenerateLoanScheduleCommand, LoanDto>
{
    public async Task<LoanDto> Handle(GenerateLoanScheduleCommand request, CancellationToken cancellationToken)
    {
        var loan = await LoanCommands.LoadAsync(repository, request.Id, cancellationToken);
        loan.GenerateSchedule(request.FirstPaymentDate, request.NumberOfPayments);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- Cancel ----
public sealed record CancelLoanCommand(Guid Id) : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class CancelLoanCommandHandler(ILoanRepository repository, IDebtsUnitOfWork unitOfWork, PaymentFactsReader factsReader)
    : IRequestHandler<CancelLoanCommand, LoanDto>
{
    public async Task<LoanDto> Handle(CancelLoanCommand request, CancellationToken cancellationToken)
    {
        var loan = await LoanCommands.LoadAsync(repository, request.Id, cancellationToken);
        loan.Cancel();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- Delete (ADR-0010 §8: only without payments) ----
public sealed record DeleteLoanCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteLoanCommandHandler(ILoanRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<DeleteLoanCommand>
{
    public async Task Handle(DeleteLoanCommand request, CancellationToken cancellationToken)
    {
        var loan = await LoanCommands.LoadAsync(repository, request.Id, cancellationToken);
        loan.EnsureCanBeDeleted();
        loan.MarkAsDeleted();
        repository.Remove(loan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- Record a payment (B4): expense from a wallet in the loan currency, created in the same transaction ----
public sealed record RecordLoanPaymentCommand(Guid LoanId, Guid WalletId, Guid OperationTypeId, decimal Amount, DateOnly PaymentDate)
    : IRequest<LoanDto>, ICommand<LoanDto>;

public sealed class RecordLoanPaymentCommandHandler(
    ILoanRepository repository,
    IDebtsUnitOfWork unitOfWork,
    PaymentFactsReader factsReader,
    ExpensePaymentRecorder paymentRecorder) : IRequestHandler<RecordLoanPaymentCommand, LoanDto>
{
    public async Task<LoanDto> Handle(RecordLoanPaymentCommand request, CancellationToken cancellationToken)
    {
        // Row lock: two concurrent payments must not both pass the "amount <= remaining" check.
        var loan = await LoanCommands.LoadAsync(repository, request.LoanId, cancellationToken);
        var walletId = new WalletId(request.WalletId);
        var operationTypeId = new OperationTypeId(request.OperationTypeId);

        var facts = await factsReader.ReadAsync(loan.Payments.Select(x => x.OperationId), cancellationToken);
        var counted = facts.CountedTotal(loan.Payments.Select(x => x.OperationId), loan.CurrencyId);

        var walletCurrency = await paymentRecorder.ValidateAsync(walletId, operationTypeId, request.PaymentDate, cancellationToken);
        loan.EnsureCanAcceptPayment(new Money(request.Amount, walletCurrency), counted);

        var operation = await paymentRecorder.RecordAsync(walletId, operationTypeId, request.Amount, request.PaymentDate, cancellationToken);
        loan.RecordPayment(operation.OperationId, walletId, new Money(operation.Amount, operation.CurrencyId), operation.OperationDate, counted);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- Get ----
public sealed record GetLoanQuery(Guid Id) : IRequest<LoanDto?>, IQuery<LoanDto?>;

public sealed class GetLoanQueryHandler(ILoanRepository repository, PaymentFactsReader factsReader)
    : IRequestHandler<GetLoanQuery, LoanDto?>
{
    public async Task<LoanDto?> Handle(GetLoanQuery request, CancellationToken cancellationToken)
    {
        var loan = await repository.GetByIdAsync(new LoanId(request.Id), cancellationToken);
        return loan is null ? null : await LoanCommands.ToDtoAsync(loan, factsReader, cancellationToken);
    }
}

// ---- List ----
public sealed record ListLoansQuery(Guid? CreditorId, string? Cursor, int Limit) : IRequest<LoanPageDto>, IQuery<LoanPageDto>;

public sealed class ListLoansQueryHandler(ILoanRepository repository, PaymentFactsReader factsReader)
    : IRequestHandler<ListLoansQuery, LoanPageDto>
{
    public async Task<LoanPageDto> Handle(ListLoansQuery request, CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(
            request.CreditorId is { } c ? new CreditorId(c) : null,
            CreatedAtCursor.TryDecode(request.Cursor), CreatedAtCursor.ClampLimit(request.Limit), cancellationToken);

        var facts = await factsReader.ReadAsync(page.Items.SelectMany(l => l.Payments.Select(p => p.OperationId)), cancellationToken);
        var today = DebtsDtoMapper.Today();
        var nextCursor = page.HasMore && page.Items.Count > 0 ? CreatedAtCursor.Encode(page.Items[^1].CreatedAt) : null;
        return new LoanPageDto(page.Items.Select(l => DebtsDtoMapper.ToDto(l, facts, today)).ToList(), nextCursor, page.HasMore);
    }
}

internal static class LoanCommands
{
    public static async Task<Loan> LoadAsync(ILoanRepository repository, Guid id, CancellationToken cancellationToken) =>
        await repository.GetForUpdateAsync(new LoanId(id), cancellationToken)
        ?? throw new KeyNotFoundException($"Loan {id} не найден.");

    public static async Task<LoanDto> ToDtoAsync(Loan loan, PaymentFactsReader factsReader, CancellationToken cancellationToken)
    {
        var facts = await factsReader.ReadAsync(loan.Payments.Select(x => x.OperationId), cancellationToken);
        return DebtsDtoMapper.ToDto(loan, facts, DebtsDtoMapper.Today());
    }
}
