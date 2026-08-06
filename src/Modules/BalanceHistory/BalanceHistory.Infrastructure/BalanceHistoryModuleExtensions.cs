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
/// Точка композиции модуля BalanceHistory (схема "balance_history") — вызывается из
/// Host/Program.cs. Регистрирует DbContext, репозиторий, сервис пересчета, фоновую задачу
/// (ADR-0004) и подписчиков на доменные события Operations (ADR-0008) — последние
/// регистрируются вручную (не через MediatR.RegisterServicesFromAssembly, который в
/// Program.cs сканирует только *.Application-сборки, а обработчики живут в Infrastructure,
/// см. OperationEventHandlers.cs).
/// </summary>
public static class BalanceHistoryModuleExtensions
{
    public static IServiceCollection AddBalanceHistoryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0006: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<BalanceHistoryDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "balance_history"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();
        services.AddScoped<IBalanceHistoryUnitOfWork, BalanceHistoryUnitOfWork>();
        services.AddScoped<BalanceRecalculationService>();

        // ADR-0009, случай a: одна из двух реализаций Wallets.Application.IWalletHistorySource.
        services.AddScoped<IWalletHistorySource, BalanceHistoryWalletHistorySource>();

        // ADR-0009, случай b: закрыт без кросс-модульного порта — решение пользователя от
        // 2026-09-11 определяет "историю" операции/перевода как дату строго раньше сегодня,
        // проверяется локально в Operations (см. DeleteOperationCommand/DeleteTransferCommand).
        // IWalletSnapshotBoundary больше не существует.

        // Направление "сверху вниз" для Reporting (W2.1, не случай ADR-0009) — см. IWalletBalanceOnDateLookup.
        services.AddScoped<IWalletBalanceOnDateLookup, WalletBalanceOnDateLookup>();

        services.AddScoped<INotificationHandler<DomainEventNotification<OperationCreated>>, OperationCreatedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationUpdated>>, OperationUpdatedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationDeleted>>, OperationDeletedHandler>();

        services.AddHostedService<BalanceSnapshotSchedulerHostedService>();

        return services;
    }
}
