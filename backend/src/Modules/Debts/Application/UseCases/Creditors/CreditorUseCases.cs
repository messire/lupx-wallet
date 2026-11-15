using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Debts.Application;

// ---- Create ----
public sealed record CreateCreditorCommand(string Name) : IRequest<CreditorDto>, ICommand<CreditorDto>;

public sealed class CreateCreditorCommandHandler(ICreditorRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<CreateCreditorCommand, CreditorDto>
{
    public async Task<CreditorDto> Handle(CreateCreditorCommand request, CancellationToken cancellationToken)
    {
        var creditor = Creditor.Create(CreditorId.New(), request.Name);
        repository.Add(creditor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditorDto.FromDomain(creditor);
    }
}

// ---- Rename ----
public sealed record RenameCreditorCommand(Guid Id, string Name) : IRequest<CreditorDto>, ICommand<CreditorDto>;

public sealed class RenameCreditorCommandHandler(ICreditorRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<RenameCreditorCommand, CreditorDto>
{
    public async Task<CreditorDto> Handle(RenameCreditorCommand request, CancellationToken cancellationToken)
    {
        var creditor = await repository.GetByIdAsync(new CreditorId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Creditor {request.Id} не найден.");

        creditor.Rename(request.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditorDto.FromDomain(creditor);
    }
}

// ---- Deactivate ----
public sealed record DeactivateCreditorCommand(Guid Id) : IRequest<CreditorDto>, ICommand<CreditorDto>;

public sealed class DeactivateCreditorCommandHandler(ICreditorRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateCreditorCommand, CreditorDto>
{
    public async Task<CreditorDto> Handle(DeactivateCreditorCommand request, CancellationToken cancellationToken)
    {
        var creditor = await repository.GetByIdAsync(new CreditorId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Creditor {request.Id} не найден.");

        creditor.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CreditorDto.FromDomain(creditor);
    }
}

// ---- Delete (only if unused, ADR-0002 §4) ----
public sealed record DeleteCreditorCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteCreditorCommandHandler(ICreditorRepository repository, IDebtsUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCreditorCommand>
{
    public async Task Handle(DeleteCreditorCommand request, CancellationToken cancellationToken)
    {
        var id = new CreditorId(request.Id);
        var creditor = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Creditor {request.Id} не найден.");

        creditor.EnsureCanBeDeleted(await repository.IsUsedAsync(id, cancellationToken));
        creditor.MarkAsDeleted();
        repository.Remove(creditor);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- List ----
public sealed record ListCreditorsQuery(bool IncludeInactive, string? Cursor, int Limit)
    : IRequest<CreditorPageDto>, IQuery<CreditorPageDto>;

public sealed class ListCreditorsQueryHandler(ICreditorRepository repository)
    : IRequestHandler<ListCreditorsQuery, CreditorPageDto>
{
    public async Task<CreditorPageDto> Handle(ListCreditorsQuery request, CancellationToken cancellationToken)
    {
        var page = await repository.ListAsync(
            request.IncludeInactive, CreditorCursor.TryDecode(request.Cursor), CreatedAtCursor.ClampLimit(request.Limit), cancellationToken);

        var nextCursor = page.HasMore && page.Items.Count > 0
            ? new CreditorCursor(page.Items[^1].Name, page.Items[^1].CreatedAt).Encode()
            : null;
        return new CreditorPageDto(page.Items.Select(CreditorDto.FromDomain).ToList(), nextCursor, page.HasMore);
    }
}
