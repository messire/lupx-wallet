using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-01 (docs/api/openapi.yaml: POST /wallets).</summary>
public sealed record CreateWalletCommand(
    string Name,
    Guid WalletTypeId,
    Guid CurrencyId,
    decimal InitialBalanceAmount,
    DateOnly AccountingStartDate,
    string? PurposeDescription,
    bool IncludeInTotal,
    int DisplayOrder,
    string? Color,
    string? Icon) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class CreateWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    PrimaryWalletPolicy primaryWalletPolicy) : IRequestHandler<CreateWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(CreateWalletCommand request, CancellationToken cancellationToken)
    {
        var isFirstWallet = await primaryWalletPolicy.IsFirstWalletAsync(cancellationToken);

        var wallet = Wallet.Create(
            WalletId.New(),
            request.Name,
            new WalletTypeId(request.WalletTypeId),
            new Money(request.InitialBalanceAmount, new CurrencyId(request.CurrencyId)),
            request.AccountingStartDate,
            request.PurposeDescription,
            request.IncludeInTotal,
            request.DisplayOrder,
            request.Color,
            request.Icon,
            isFirstWallet);

        repository.Add(wallet);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
