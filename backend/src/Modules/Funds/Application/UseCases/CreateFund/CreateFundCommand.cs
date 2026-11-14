using LupexWallet.Funds.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>POST /funds (ADR-0010 §4). The currency must be active; debt/loan links are not validated yet.</summary>
public sealed record CreateFundCommand(
    string Name,
    decimal TargetAmount,
    Guid CurrencyId,
    Guid? LinkedDebtId,
    Guid? LinkedLoanId) : IRequest<FundDto>, ICommand<FundDto>;

public sealed class CreateFundCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    ICurrencyLookup currencyLookup,
    FundViewBuilder viewBuilder) : IRequestHandler<CreateFundCommand, FundDto>
{
    public async Task<FundDto> Handle(CreateFundCommand request, CancellationToken cancellationToken)
    {
        var currencyId = new CurrencyId(request.CurrencyId);
        _ = await currencyLookup.GetAsync(currencyId, cancellationToken) ?? throw new FundCurrencyNotFoundException();

        var fund = Fund.Create(
            FundId.New(), request.Name, request.TargetAmount, currencyId, request.LinkedDebtId, request.LinkedLoanId);

        repository.Add(fund);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
