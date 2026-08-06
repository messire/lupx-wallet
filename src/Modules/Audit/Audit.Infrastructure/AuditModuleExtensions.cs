using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Точка композиции модуля Audit (схема "audit") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля Audit
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
