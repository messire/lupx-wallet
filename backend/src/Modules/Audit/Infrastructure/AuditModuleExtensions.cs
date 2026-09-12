using LupexWallet.Audit.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Composition root of the Audit module (schema "audit") — called from Host/Program.cs.
/// Registers the DbContext, the repository, and subscribers to domain events of all modules
/// (ADR-0010) — handlers live in Infrastructure and are registered manually (not via
/// MediatR.RegisterServicesFromAssembly, which in Program.cs only scans *.Application
/// assemblies, see ADR-0008/OperationEventHandlers.cs).
/// </summary>
public static class AuditModuleExtensions
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0006: одна база, схема на модуль).");

        // AuditEntry не поднимает собственных доменных событий (append-only, ADR-0010) —
        // DispatchDomainEventsInterceptor на AuditDbContext не подключается, но сам класс
        // интерцептора (общий на весь DI-scope, см. ADR-0008/ADR-0010) внедряется в
        // AuditRecorder напрямую как обычный Scoped-сервис — регистрация ниже.
        services.AddDbContext<AuditDbContext>((_, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "audit"))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();
        services.AddScoped<IAuditUnitOfWork, AuditUnitOfWork>();
        services.AddScoped<AuditRecorder>();

        RegisterEventHandlers(services);

        return services;
    }

    private static void RegisterEventHandlers(IServiceCollection services)
    {
        // Wallets (WalletCreated/Updated/Archived/Deleted, PrimaryWalletChanged,
        // WalletCurrencyChanged, WalletBalanceChanged).
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCreated>>, WalletCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletUpdated>>, WalletUpdatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletArchived>>, WalletArchivedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletDeleted>>, WalletDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PrimaryWalletChanged>>, PrimaryWalletChangedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>, WalletCurrencyChangedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletBalanceChanged>>, WalletBalanceChangedAuditHandler>();

        // ReferenceData (3 типа x create/deactivate/delete = 9 событий).
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletTypeCreated>>, WalletTypeCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletTypeDeactivated>>, WalletTypeDeactivatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletTypeDeleted>>, WalletTypeDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationTypeCreated>>, OperationTypeCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationTypeDeactivated>>, OperationTypeDeactivatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationTypeDeleted>>, OperationTypeDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<CurrencyCreated>>, CurrencyCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<CurrencyDeactivated>>, CurrencyDeactivatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<CurrencyDeleted>>, CurrencyDeletedAuditHandler>();

        // Operations (OperationCreated/Updated/Deleted, TransferCreated/Deleted).
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationCreated>>, OperationCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationUpdated>>, OperationUpdatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<OperationDeleted>>, OperationDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<TransferCreated>>, TransferCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<TransferDeleted>>, TransferDeletedAuditHandler>();

        // BalanceHistory (BalanceSnapshotCreated/Updated).
        services.AddScoped<INotificationHandler<DomainEventNotification<BalanceSnapshotCreated>>, BalanceSnapshotCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<BalanceSnapshotUpdated>>, BalanceSnapshotUpdatedAuditHandler>();

        // ExchangeRates (ExchangeRatesUpdated, ExchangeRateUpdateFailed).
        services.AddScoped<INotificationHandler<DomainEventNotification<ExchangeRatesUpdated>>, ExchangeRatesUpdatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<ExchangeRateUpdateFailed>>, ExchangeRateUpdateFailedAuditHandler>();
    }
}
