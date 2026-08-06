using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Точка композиции модуля ReferenceData (схема "reference_data") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозитории и обработчики команд/запросов модуля.
/// TODO: наполняется по мере реализации вертикальных срезов модуля ReferenceData
/// (см. docs/architecture/ddd-model.md, docs/database/schema.md).
/// </summary>
public static class ReferenceDataModuleExtensions
{
    public static IServiceCollection AddReferenceDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
