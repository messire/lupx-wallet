using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ReferenceData.Application;

public sealed record CurrencyDto(Guid Id, string Code, string Name, bool IsActive)
{
    public static CurrencyDto FromDomain(Currency entity) => new(entity.Id.Value, entity.Code, entity.Name, entity.IsActive);
}

public sealed record CurrencyPageDto(IReadOnlyList<CurrencyDto> Data, string? NextCursor, bool HasMore);

public sealed record CurrencyPageResult(IReadOnlyList<Currency> Items, bool HasMore);

public interface ICurrencyRepository
{
    void Add(Currency currency);
    void Remove(Currency currency);
    Task<Currency?> GetByIdAsync(CurrencyId id, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);
    Task<CurrencyPageResult> ListAsync(bool includeInactive, NameCursor? afterCursor, int limit, CancellationToken cancellationToken);
}

// ---- Добавление валюты, включая криптовалюту (решено при проектировании домена) ----
public sealed record CreateCurrencyCommand(string Code, string Name) : IRequest<CurrencyDto>, ICommand<CurrencyDto>;

public sealed class CreateCurrencyCommandHandler(ICurrencyRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<CreateCurrencyCommand, CurrencyDto>
{
    public async Task<CurrencyDto> Handle(CreateCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (await repository.CodeExistsAsync(request.Code, cancellationToken))
        {
            throw new CurrencyCodeAlreadyExistsException(request.Code);
        }

        var currency = Currency.Create(CurrencyId.New(), request.Code, request.Name);
        repository.Add(currency);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CurrencyDto.FromDomain(currency);
    }
}

public sealed record DeactivateCurrencyCommand(Guid Id) : IRequest<CurrencyDto>, ICommand<CurrencyDto>;

public sealed class DeactivateCurrencyCommandHandler(ICurrencyRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateCurrencyCommand, CurrencyDto>
{
    public async Task<CurrencyDto> Handle(DeactivateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var currency = await repository.GetByIdAsync(new CurrencyId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Currency {request.Id} не найдена.");

        currency.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CurrencyDto.FromDomain(currency);
    }
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

        // ADR-0009, случай c: реальная проверка использования через реализации Wallets/
        // Operations (и ExchangeRates, если его пробник уже зарегистрирован к моменту
        // сборки Host — см. W1.1/"Известное упрощение", ExchangeRatesReferenceItemUsageProbe).
        var isUsed = await usageProbes.AnyIsUsedAsync(ReferenceItemKind.Currency, request.Id, cancellationToken);
        currency.EnsureCanBeDeleted(isUsed);

        currency.MarkAsDeleted();
        repository.Remove(currency);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- UC-... список валют ----
public sealed record ListCurrenciesQuery(bool IncludeInactive, string? Cursor, int Limit)
    : IRequest<CurrencyPageDto>, IQuery<CurrencyPageDto>;

public sealed class ListCurrenciesQueryHandler(ICurrencyRepository repository)
    : IRequestHandler<ListCurrenciesQuery, CurrencyPageDto>
{
    public async Task<CurrencyPageDto> Handle(ListCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var cursor = NameCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(request.IncludeInactive, cursor, limit, cancellationToken);

        var items = page.Items.Select(CurrencyDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new NameCursor(items[^1].Name, page.Items[^1].CreatedAt).Encode()
            : null;

        return new CurrencyPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
