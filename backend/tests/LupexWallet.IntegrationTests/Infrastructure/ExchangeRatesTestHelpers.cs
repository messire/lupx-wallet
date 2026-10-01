using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Runs what ExchangeRateRefreshBackgroundService does in production. The test host removes every
/// IHostedService (LupexWalletApiFactory), so a test that needs the history load — or a refresh in
/// the System actor's name — drives the commands itself, each in its own scope like the real service.
/// </summary>
public static class ExchangeRatesTestHelpers
{
    /// <summary>Refresh, then history load — one background pass; actor System.</summary>
    public static async Task<(RefreshExchangeRatesResult Refresh, BackfillResult Backfill)> RunExchangeRatesBackgroundPassAsync(
        this LupexWalletApiFactory factory, CancellationToken cancellationToken = default)
    {
        var refresh = await SendInOwnScopeAsync(factory, new RefreshExchangeRatesCommand(IsManualTrigger: false), cancellationToken);
        var backfill = await SendInOwnScopeAsync(factory, new BackfillExchangeRateHistoryCommand(), cancellationToken);
        return (refresh, backfill);
    }

    public static Task<(RefreshExchangeRatesResult Refresh, BackfillResult Backfill)> RunExchangeRatesBackgroundPassAsync(
        this ApiTestFixture fixture, CancellationToken cancellationToken = default) =>
        fixture.Factory.RunExchangeRatesBackgroundPassAsync(cancellationToken);

    private static async Task<TResponse> SendInOwnScopeAsync<TResponse>(
        LupexWalletApiFactory factory, IRequest<TResponse> command, CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>().SetSystemActor("ExchangeRatesTestPass");
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }
}
