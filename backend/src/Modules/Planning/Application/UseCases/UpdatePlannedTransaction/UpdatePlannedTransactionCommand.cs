using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>PATCH /planned-transactions/{id} — all fields are passed in full, like the other PATCH endpoints.</summary>
public sealed record UpdatePlannedTransactionCommand(
    Guid Id,
    Guid OperationTypeId,
    decimal Amount,
    Guid CurrencyId,
    Guid? WalletId,
    string? Description,
    DateOnly StartDate,
    RecurrenceInput? Recurrence) : IRequest<PlannedTransactionDto>, ICommand<PlannedTransactionDto>;

public sealed class UpdatePlannedTransactionCommandHandler(
    IPlannedTransactionRepository repository,
    IPlanningUnitOfWork unitOfWork,
    PlannedTransactionReferences references) : IRequestHandler<UpdatePlannedTransactionCommand, PlannedTransactionDto>
{
    public async Task<PlannedTransactionDto> Handle(UpdatePlannedTransactionCommand request, CancellationToken cancellationToken)
    {
        var plan = await repository.GetForUpdateAsync(new PlannedTransactionId(request.Id), cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

        if (!plan.IsActive)
        {
            throw new PlannedTransactionFinishedException();
        }

        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var currencyId = new CurrencyId(request.CurrencyId);
        var walletId = request.WalletId is { } id ? new WalletId(id) : (WalletId?)null;

        var direction = await references.ResolveDirectionAsync(operationTypeId, plan.OperationTypeId, cancellationToken);
        await references.EnsureCurrencyAsync(currencyId, plan.CurrencyId, cancellationToken);
        if (walletId is { } wallet)
        {
            await references.GetWalletInCurrencyAsync(wallet, currencyId, cancellationToken);
        }

        plan.Update(
            operationTypeId, direction, request.Amount, currencyId, walletId,
            request.Description, request.StartDate, request.Recurrence?.ToRule());

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlannedTransactionDto.FromDomain(plan);
    }
}
