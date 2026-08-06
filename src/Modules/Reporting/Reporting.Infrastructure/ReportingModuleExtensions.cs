using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Reporting.Infrastructure;

/// <summary>
/// Точка композиции модуля Reporting (без собственной схемы — только чтение через контракты
/// других модулей) — вызывается из Host/Program.cs. Намеренно не регистрирует ничего своего
/// (W2.1, GetTotalAmountQuery): Reporting.Application ссылается напрямую на порты
/// Wallets.Application.IWalletTotalsSource, BalanceHistory.Application.IWalletBalanceOnDateLookup
/// и ExchangeRates.Application.IExchangeRateLookup, а их реализации уже регистрируются
/// AddWalletsModule/AddBalanceHistoryModule/AddExchangeRatesModule соответственно
/// (направление "сверху вниз" — не случай ADR-0009, где потребитель регистрирует
/// собственный порт при отсутствующей реализации). Обработчик GetTotalAmountQueryHandler
/// находится в Reporting.Application и подхватывается MediatR.RegisterServicesFromAssembly
/// в Program.cs (AssemblyMarker уже там зарегистрирован).
/// </summary>
public static class ReportingModuleExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
