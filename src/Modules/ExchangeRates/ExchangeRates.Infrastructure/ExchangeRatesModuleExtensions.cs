using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Точка композиции модуля ExchangeRates (схема "exchange_rates") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля ExchangeRates
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class ExchangeRatesModuleExtensions
{
    public static IServiceCollection AddExchangeRatesModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
