using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Точка композиции модуля Operations (схема "operations") — вызывается из
/// Host/Program.cs. Регистрирует DbContext, репозитории и обработчики команд/запросов.
/// </summary>
public static class OperationsModuleExtensions
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0006: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<OperationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOperationRepository, OperationRepository>();
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<IOperationsUnitOfWork, OperationsUnitOfWork>();
        services.AddScoped<IWalletOperationsLookup, WalletOperationsLookup>();

        // ADR-0009, случай a: одна из двух реализаций Wallets.Application.IWalletHistorySource.
        services.AddScoped<IWalletHistorySource, OperationsWalletHistorySource>();

        // ADR-0009, случай c: одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe.
        services.AddScoped<IReferenceItemUsageProbe, OperationsReferenceItemUsageProbe>();

        return services;
    }
}
