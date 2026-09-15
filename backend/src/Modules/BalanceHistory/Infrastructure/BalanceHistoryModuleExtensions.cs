using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using LupexWallet.Wallets.Application;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Composition root of the BalanceHistory module (schema "balance_history") — invoked from
/// Host/Program.cs. Registers the DbContext, the repository, the recalculation service, the
/// background job (ADR-0003), and the Operations domain event subscribers (ADR-0003) — the
/// latter are registered manually (not via MediatR.RegisterServicesFromAssembly, which in
/// Program.cs only scans *.Application assemblies, while the handlers live in Infrastructure,
/// see OperationEventHandlers.cs).
/// </summary>
public static class BalanceHistoryModuleExtensions
{
    public static IServiceCollection AddBalanceHistoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<BalanceHistoryDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "balance_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();
        services.AddScoped<IBalanceHistoryUnitOfWork, BalanceHistoryUnitOfWork>();
        services.AddScoped<BalanceRecalculationService>();

        // ADR-0002, §2: одна из двух реализаций Wallets.Application.IWalletHistorySource.
        services.AddScoped<IWalletHistorySource, BalanceHistoryWalletHistorySource>();

        // Проверка даты при удалении операции (прежнее правило до мягкого удаления, ADR-0002, §3)
        // выполняется без кросс-модульного порта — решение пользователя от
        // 2026-09-11 определяет "историю" операции/перевода как дату строго раньше сегодня,
        // проверяется локально в Operations (см. DeleteOperationCommand/DeleteTransferCommand).
        // IWalletSnapshotBoundary больше не существует.

        // Направление "сверху вниз" для Reporting (W2.1, порт у владельца — ADR-0001, §2, способ c) — см. IWalletBalanceOnDateLookup.
        services.AddScoped<IWalletBalanceOnDateLookup, WalletBalanceOnDateLookup>();

        services.AddScoped<INotificationHandler<DomainEventNotification<OperationCreated>>, OperationCreatedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationUpdated>>, OperationUpdatedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationDeleted>>, OperationDeletedHandler>();

        services.AddHostedService<BalanceSnapshotSchedulerHostedService>();

        return services;
    }
}
