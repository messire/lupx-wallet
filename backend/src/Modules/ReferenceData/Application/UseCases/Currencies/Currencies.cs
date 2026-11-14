using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ReferenceData.Application;

public sealed record CurrencyDto(Guid Id, string Code, string Name)
{
    public static CurrencyDto FromDomain(Currency entity) => new(entity.Id.Value, entity.Code, entity.Name);
}

public sealed record CurrencyPageDto(IReadOnlyList<CurrencyDto> Data, string? NextCursor, bool HasMore);

public sealed record CurrencyPageResult(IReadOnlyList<Currency> Items, bool HasMore);

public interface ICurrencyRepository
{
    void Add(Currency currency);
    void Remove(Currency currency);
    Task<Currency?> GetByIdAsync(CurrencyId id, CancellationToken cancellationToken);
    Task<Currency?> GetByCodeAsync(string code, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);
    Task<CurrencyPageResult> ListAsync(NameCursor? afterCursor, int limit, CancellationToken cancellationToken);
}

public sealed record AvailableCurrencyDto(string Code, string Name, bool IsActive);

// ---- Add a currency to the active set from the available list (ADR-0004 §7) ----
public sealed record ActivateCurrencyCommand(string Code) : IRequest<CurrencyDto>, ICommand<CurrencyDto>;

public sealed class ActivateCurrencyCommandHandler(
    CurrencyActivationPolicy activationPolicy,
    IReferenceDataUnitOfWork unitOfWork) : IRequestHandler<ActivateCurrencyCommand, CurrencyDto>
{
    public async Task<CurrencyDto> Handle(ActivateCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            await unitOfWork.LockCurrencyCodeAsync(request.Code, cancellationToken);
        }

        var currency = await activationPolicy.ActivateAsync(request.Code, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CurrencyDto.FromDomain(currency);
    }
}

// ---- Available currencies: the rate service list stored in the database ----
public sealed record ListAvailableCurrenciesQuery
    : IRequest<IReadOnlyList<AvailableCurrencyDto>>, IQuery<IReadOnlyList<AvailableCurrencyDto>>;

public sealed class ListAvailableCurrenciesQueryHandler(IAvailableCurrencyRepository repository)
    : IRequestHandler<ListAvailableCurrenciesQuery, IReadOnlyList<AvailableCurrencyDto>>
{
    public Task<IReadOnlyList<AvailableCurrencyDto>> Handle(
        ListAvailableCurrenciesQuery request, CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);
}

public sealed record DeleteCurrencyCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteCurrencyCommandHandler(
    ICurrencyRepository repository,
    IReferenceDataUnitOfWork unitOfWork,
    IEnumerable<IReferenceItemUsageProbe> usageProbes) : IRequestHandler<DeleteCurrencyCommand>
{
    public async Task Handle(DeleteCurrencyCommand request, CancellationToken cancellationToken)
    {
        var currency = await repository.GetByIdAsync(new CurrencyId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Currency {request.Id} не найдена.");

        // Wallet creation takes the same lock before using an active currency, so a wallet cannot
        // appear between the usage check and the removal (ADR-0004 §7).
        await unitOfWork.LockCurrencyCodeAsync(currency.Code, cancellationToken);

        // ADR-0002 §4: use = wallets (archived included) and operations (soft-deleted included)
        // via the two port implementations; exchange rates are not usage — the ExchangeRates
        // CurrencyDeletedHandler removes them in this command's transaction.
        var isUsed = await usageProbes.AnyIsUsedAsync(ReferenceItemKind.Currency, request.Id, cancellationToken);
        currency.EnsureCanBeDeleted(isUsed);

        currency.MarkAsDeleted();
        repository.Remove(currency);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- UC-... список валют ----
public sealed record ListCurrenciesQuery(string? Cursor, int Limit)
    : IRequest<CurrencyPageDto>, IQuery<CurrencyPageDto>;

public sealed class ListCurrenciesQueryHandler(ICurrencyRepository repository)
    : IRequestHandler<ListCurrenciesQuery, CurrencyPageDto>
{
    public async Task<CurrencyPageDto> Handle(ListCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var cursor = NameCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(cursor, limit, cancellationToken);

        var items = page.Items.Select(CurrencyDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new NameCursor(items[^1].Name, page.Items[^1].CreatedAt).Encode()
            : null;

        return new CurrencyPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
