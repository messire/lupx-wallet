using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-14 (docs/api/openapi.yaml: DELETE /operations/{id}).</summary>
public sealed record DeleteOperationCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<DeleteOperationCommand>
{
    public async Task Handle(DeleteOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByIdAsync(new OperationId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        // Прежнее правило (решение от 2026-09-11), действует до реализации мягкого удаления —
        // ADR-0002, §3 и «Состояние реализации». Граница "история" для
        // удаления операции — сегодняшний день, без обращения к BalanceHistory (кросс-модульный
        // запрос не нужен). Каскадный пересчет (ADR-0003) материализует слепок на дату
        // операции немедленно, поэтому "последний слепок кошелька" почти всегда ≥ дате любой
        // существующей операции — буквальная проверка через него заблокировала бы почти все
        // удаления. Вместо этого: дата строго раньше сегодня — уже отражена в истории и не
        // подлежит удалению (только редактирование, UC-13); дата = сегодня — удаление разрешено.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hasHistory = operation.OperationDate < today;
        operation.EnsureCanBeDeleted(hasHistory);

        var reversal = operation.AppliedDelta.Negate();
        await walletBalanceGateway.ApplyDeltaAsync(operation.WalletId, reversal, cancellationToken);

        operation.MarkAsDeleted();
        repository.Remove(operation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
