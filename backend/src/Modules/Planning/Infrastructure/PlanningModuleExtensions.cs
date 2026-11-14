using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Planning.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>Composition root of the Planning module (schema "planning", ADR-0010 §6) — called from Host/Program.cs.</summary>
public static class PlanningModuleExtensions
{
    public static IServiceCollection AddPlanningModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<PlanningDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PlanningDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IPlannedTransactionRepository, PlannedTransactionRepository>();
        services.AddScoped<IPlanningUnitOfWork, PlanningUnitOfWork>();
        services.AddScoped<PlannedTransactionReferences>();
        services.AddScoped<PlannedOccurrenceReader>();

        // ADR-0010 §7: read-only port for Reporting.
        services.AddScoped<IPlannedOccurrencesSource, PlannedOccurrencesSource>();

        // ADR-0002 §4: an operation type or currency of a plan counts as used.
        services.AddScoped<IReferenceItemUsageProbe, PlanningReferenceItemUsageProbe>();

        // Registered manually: MediatR scans only *.Application assemblies in Program.cs.
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletDeleted>>, PlanningWalletDeletedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>, PlanningWalletCurrencyChangedHandler>();

        return services;
    }
}
