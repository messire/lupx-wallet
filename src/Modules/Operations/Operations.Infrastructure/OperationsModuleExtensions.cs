using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Точка композиции модуля Operations (схема "operations") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля Operations
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class OperationsModuleExtensions
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
