using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

// Deviations of one occurrence (ADR-0010 §6, OccurrenceOverride). OccurrenceDate is always the
// original date from the schedule, also after a move. Each command returns the recomputed occurrence.

/// <summary>POST /planned-transactions/{id}/occurrences/{date}/skip.</summary>
public sealed record SkipPlannedOccurrenceCommand(Guid PlannedTransactionId, DateOnly OccurrenceDate)
    : IRequest<PlannedOccurrenceDto>, ICommand<PlannedOccurrenceDto>;

/// <summary>POST /planned-transactions/{id}/occurrences/{date}/unskip.</summary>
public sealed record UnskipPlannedOccurrenceCommand(Guid PlannedTransactionId, DateOnly OccurrenceDate)
    : IRequest<PlannedOccurrenceDto>, ICommand<PlannedOccurrenceDto>;

/// <summary>POST /planned-transactions/{id}/occurrences/{date}/move; NewDate equal to the original date undoes the move.</summary>
public sealed record MovePlannedOccurrenceCommand(Guid PlannedTransactionId, DateOnly OccurrenceDate, DateOnly NewDate)
    : IRequest<PlannedOccurrenceDto>, ICommand<PlannedOccurrenceDto>;

/// <summary>PUT /planned-transactions/{id}/occurrences/{date}/amount; null Amount returns to the plan amount.</summary>
public sealed record ChangePlannedOccurrenceAmountCommand(Guid PlannedTransactionId, DateOnly OccurrenceDate, decimal? Amount)
    : IRequest<PlannedOccurrenceDto>, ICommand<PlannedOccurrenceDto>;

public sealed class ChangePlannedOccurrenceCommandHandlers(
    IPlannedTransactionRepository repository,
    IPlanningUnitOfWork unitOfWork,
    PlannedOccurrenceReader reader) :
    IRequestHandler<SkipPlannedOccurrenceCommand, PlannedOccurrenceDto>,
    IRequestHandler<UnskipPlannedOccurrenceCommand, PlannedOccurrenceDto>,
    IRequestHandler<MovePlannedOccurrenceCommand, PlannedOccurrenceDto>,
    IRequestHandler<ChangePlannedOccurrenceAmountCommand, PlannedOccurrenceDto>
{
    public Task<PlannedOccurrenceDto> Handle(SkipPlannedOccurrenceCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.PlannedTransactionId, request.OccurrenceDate, (plan, current) => plan.SkipOccurrence(current), cancellationToken);

    public Task<PlannedOccurrenceDto> Handle(UnskipPlannedOccurrenceCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.PlannedTransactionId, request.OccurrenceDate, (plan, current) => plan.UnskipOccurrence(current), cancellationToken);

    public Task<PlannedOccurrenceDto> Handle(MovePlannedOccurrenceCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(
            request.PlannedTransactionId, request.OccurrenceDate,
            (plan, current) => plan.MoveOccurrence(current, request.NewDate), cancellationToken);

    public Task<PlannedOccurrenceDto> Handle(ChangePlannedOccurrenceAmountCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(
            request.PlannedTransactionId, request.OccurrenceDate,
            (plan, current) => plan.ChangeOccurrenceAmount(current, request.Amount), cancellationToken);

    private async Task<PlannedOccurrenceDto> ChangeAsync(
        Guid planId, DateOnly occurrenceDate, Action<PlannedTransaction, PlannedOccurrence> change, CancellationToken cancellationToken)
    {
        var plan = await repository.GetForUpdateAsync(new PlannedTransactionId(planId), cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

        var current = await reader.ProjectOneAsync(plan, occurrenceDate, cancellationToken);
        change(plan, current);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await reader.ProjectOneAsync(plan, occurrenceDate, cancellationToken);
        return PlannedOccurrenceDto.From(plan, updated);
    }
}
