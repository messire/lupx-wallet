using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ReferenceData.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Точка композиции модуля ReferenceData (схема "reference_data") — вызывается из
/// Host/Program.cs. Регистрирует DbContext, репозитории и обработчики команд/запросов.
/// </summary>
public static class ReferenceDataModuleExtensions
{
    public static IServiceCollection AddReferenceDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0006: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<ReferenceDataDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "reference_data"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IWalletTypeRepository, WalletTypeRepository>();
        services.AddScoped<IOperationTypeRepository, OperationTypeRepository>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IOperationBehaviorKindRepository, OperationBehaviorKindRepository>();
        services.AddScoped<IReferenceDataUnitOfWork, ReferenceDataUnitOfWork>();
        services.AddScoped<IOperationTypeLookup, OperationTypeLookup>();
        services.AddScoped<ICurrencyLookup, CurrencyLookup>();

        // W2.5, случай d: разрешение типа кошелька для CreateWalletCommand/UpdateWalletCommand.
        services.AddScoped<IWalletTypeLookup, WalletTypeLookup>();

        return services;
    }
}
