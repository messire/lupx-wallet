using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Runs what StatedBalanceReconciliationHostedService does at startup. The test host removes every
/// IHostedService (LupexWalletApiFactory), so a test that needs the reconciliation drives the
/// command itself, in its own scope under the System actor, like the real service.
/// </summary>
public static class StatedBalanceTestHelpers
{
    public static async Task RunCorrectionsReconciliationAsync(
        this LupexWalletApiFactory factory, Guid walletId, CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>().SetSystemActor("StatedBalanceTestReconciliation");
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReconcileWalletCorrectionsCommand(walletId), cancellationToken);
    }

    public static Task RunCorrectionsReconciliationAsync(
        this ApiTestFixture fixture, Guid walletId, CancellationToken cancellationToken = default) =>
        fixture.Factory.RunCorrectionsReconciliationAsync(walletId, cancellationToken);
}
