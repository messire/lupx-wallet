using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// Composition root of the Operations module (schema "operations") — called from
/// Host/Program.cs. Registers the DbContext, repositories, and command/query handlers.
/// </summary>
public static class OperationsModuleExtensions
{
    public static IServiceCollection AddOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<OperationsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "operations"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOperationRepository, OperationRepository>();
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<IOperationsUnitOfWork, OperationsUnitOfWork>();
        services.AddScoped<IWalletOperationsLookup, WalletOperationsLookup>();
        services.AddScoped<IWalletCorrectionsReconciler, WalletCorrectionsReconciler>();

        // ADR-0002, §2: единственная реализация Wallets.Application.IWalletHistorySource
        // (удалённые операции считаются историей).
        services.AddScoped<IWalletHistorySource, OperationsWalletHistorySource>();

        // ADR-0002, §4: одна из двух реализаций ReferenceData.Application.IReferenceItemUsageProbe
        // (удалённые операции считаются использованием).
        services.AddScoped<IReferenceItemUsageProbe, OperationsReferenceItemUsageProbe>();

        // ADR-0002, §2: balance corrections are removed with the wallet and follow its currency.
        // Registered manually: MediatR scans only *.Application assemblies in Program.cs.
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletDeleted>>, WalletDeletedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>, WalletCurrencyChangedHandler>();

        // ADR-0003, §6: startup reconciliation of the balance corrections.
        services.AddHostedService<StatedBalanceReconciliationHostedService>();

        return services;
    }
}
