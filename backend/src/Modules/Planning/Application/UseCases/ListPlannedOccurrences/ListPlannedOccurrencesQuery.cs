using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>
/// GET /planned-occurrences — computed occurrences of all plans (or one plan) whose effective date is
/// in [From, To]. The range is limited to <see cref="MaxRangeDays"/> days, since results are not paginated.
/// </summary>
public sealed record ListPlannedOccurrencesQuery(DateOnly From, DateOnly To, Guid? PlannedTransactionId)
    : IRequest<PlannedOccurrenceListDto>, IQuery<PlannedOccurrenceListDto>
{
    public const int MaxRangeDays = 731;
}

public sealed class ListPlannedOccurrencesQueryHandler(IPlannedTransactionRepository repository, PlannedOccurrenceReader reader)
    : IRequestHandler<ListPlannedOccurrencesQuery, PlannedOccurrenceListDto>
{
    public async Task<PlannedOccurrenceListDto> Handle(ListPlannedOccurrencesQuery request, CancellationToken cancellationToken)
    {
        if (request.From > request.To)
        {
            throw new PlannedOccurrenceRangeInvalidException("Начало периода не может быть позже его конца.");
        }

        if (request.To.DayNumber - request.From.DayNumber + 1 > ListPlannedOccurrencesQuery.MaxRangeDays)
        {
            throw new PlannedOccurrenceRangeInvalidException(
                $"Период — не длиннее {ListPlannedOccurrencesQuery.MaxRangeDays} дней.");
        }

        var onlyId = request.PlannedTransactionId is { } id ? new PlannedTransactionId(id) : (PlannedTransactionId?)null;
        var plans = await repository.ListAllAsync(onlyId, cancellationToken);
        if (onlyId is not null && plans.Count == 0)
        {
            throw new PlannedTransactionNotFoundException();
        }

        var occurrences = await reader.ProjectAsync(plans, request.From, request.To, cancellationToken);
        return new PlannedOccurrenceListDto(
            request.From, request.To, occurrences.Select(x => PlannedOccurrenceDto.From(x.Plan, x.Occurrence)).ToList());
    }
}
