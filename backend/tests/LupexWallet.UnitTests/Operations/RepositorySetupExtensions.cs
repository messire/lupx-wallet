using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using NSubstitute;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// Handlers first look up the wallets of a record without tracking (to take the wallet locks) and
/// only then load the aggregate; these helpers set up both reads for a mocked repository.
/// </summary>
internal static class RepositorySetupExtensions
{
    public static void Found(this IOperationRepository repository, Operation operation)
    {
        repository.FindWalletIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation.WalletId);
        repository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
    }

    public static void Found(this ITransferRepository repository, Transfer transfer)
    {
        repository.FindWalletIdsAsync(transfer.Id, Arg.Any<CancellationToken>())
            .Returns(new TransferWalletIds(transfer.SourceWalletId, transfer.TargetWalletId));
        repository.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);
    }
}
