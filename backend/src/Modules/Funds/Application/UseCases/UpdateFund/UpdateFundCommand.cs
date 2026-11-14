using LupexWallet.Funds.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>
/// PATCH /funds/{id}: all editable fields are passed in full. The currency can change only
/// before the first reserve; a new currency must be active.
/// </summary>
public sealed record UpdateFundCommand(
    Guid Id,
    string Name,
    decimal TargetAmount,
    Guid CurrencyId,
    Guid? LinkedDebtId,
    Guid? LinkedLoanId) : IRequest<FundDto>, ICommand<FundDto>;

public sealed class UpdateFundCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    ICurrencyLookup currencyLookup,
    FundViewBuilder viewBuilder) : IRequestHandler<UpdateFundCommand, FundDto>
{
    public async Task<FundDto> Handle(UpdateFundCommand request, CancellationToken cancellationToken)
    {
        var fund = await repository.GetByIdAsync(new FundId(request.Id), cancellationToken)
            ?? throw new FundNotFoundException();

        var currencyId = new CurrencyId(request.CurrencyId);
        if (currencyId != fund.CurrencyId)
        {
            _ = await currencyLookup.GetAsync(currencyId, cancellationToken) ?? throw new FundCurrencyNotFoundException();
        }

        fund.Update(request.Name, request.TargetAmount, currencyId, request.LinkedDebtId, request.LinkedLoanId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
