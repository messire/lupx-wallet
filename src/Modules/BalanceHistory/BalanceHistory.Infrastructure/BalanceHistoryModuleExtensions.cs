using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Точка композиции модуля BalanceHistory (схема "balance_history") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля BalanceHistory
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class BalanceHistoryModuleExtensions
{
    public static IServiceCollection AddBalanceHistoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
