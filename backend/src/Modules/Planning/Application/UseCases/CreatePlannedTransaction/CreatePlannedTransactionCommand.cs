using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>Repetition input; a missing interval means 1.</summary>
public sealed record RecurrenceInput(RecurrenceFrequency Frequency, int? Interval, DateOnly? Until, int? Count)
{
    public RecurrenceRule ToRule() => RecurrenceRule.Create(Frequency, Interval ?? 1, Until, Count);
}

/// <summary>POST /planned-transactions (ADR-0010 §6).</summary>
public sealed record CreatePlannedTransactionCommand(
    Guid OperationTypeId,
    decimal Amount,
    Guid CurrencyId,
    Guid? WalletId,
    string? Description,
    DateOnly StartDate,
    RecurrenceInput? Recurrence) : IRequest<PlannedTransactionDto>, ICommand<PlannedTransactionDto>;

public sealed class CreatePlannedTransactionCommandHandler(
    IPlannedTransactionRepository repository,
    IPlanningUnitOfWork unitOfWork,
    PlannedTransactionReferences references) : IRequestHandler<CreatePlannedTransactionCommand, PlannedTransactionDto>
{
    public async Task<PlannedTransactionDto> Handle(CreatePlannedTransactionCommand request, CancellationToken cancellationToken)
    {
        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var currencyId = new CurrencyId(request.CurrencyId);
        var walletId = request.WalletId is { } id ? new WalletId(id) : (WalletId?)null;

        var direction = await references.ResolveDirectionAsync(operationTypeId, currentOperationTypeId: null, cancellationToken);
        await references.EnsureCurrencyAsync(currencyId, currentCurrencyId: null, cancellationToken);
        if (walletId is { } wallet)
        {
            await references.GetWalletInCurrencyAsync(wallet, currencyId, cancellationToken);
        }

        var plan = PlannedTransaction.Create(
            PlannedTransactionId.New(), operationTypeId, direction, request.Amount, currencyId, walletId,
            request.Description, request.StartDate, request.Recurrence?.ToRule());

        repository.Add(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlannedTransactionDto.FromDomain(plan);
    }
}
