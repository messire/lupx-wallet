using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Reporting.Infrastructure;

/// <summary>
/// Точка композиции модуля Reporting (без собственной схемы — только чтение через контракты других модулей) — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля Reporting
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class ReportingModuleExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
