using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Reporting.Infrastructure;

/// <summary>
/// Composition root of the Reporting module (no schema of its own — reads only through other
/// modules' contracts) — called from Host/Program.cs. Intentionally registers nothing: Reporting.Application
/// references Wallets.Application.IWalletTotalsSource, BalanceHistory.Application.IWalletBalanceOnDateLookup
/// and ExchangeRates.Application.IExchangeRateLookup directly, and their implementations are already
/// registered by AddWalletsModule/AddBalanceHistoryModule/AddExchangeRatesModule (top-down direction,
/// not the ADR-0009 case where the consumer registers its own port for a missing implementation).
/// GetTotalAmountQueryHandler lives in Reporting.Application and is picked up by
/// MediatR.RegisterServicesFromAssembly in Program.cs via the already-registered AssemblyMarker.
/// </summary>
public static class ReportingModuleExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
