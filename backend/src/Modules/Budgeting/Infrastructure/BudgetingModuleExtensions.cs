using LupexWallet.Budgeting.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ReferenceData.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LupexWallet.Budgeting.Infrastructure;

/// <summary>
/// Composition root of the Budgeting module (schema "budgeting", ADR-0010 §5) — called from Host/Program.cs.
/// Reads operation facts, operation types, currencies and rates through ports registered by their owners.
/// </summary>
public static class BudgetingModuleExtensions
{
    public static IServiceCollection AddBudgetingModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.TryAddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<BudgetingDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", BudgetingDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IExpenseCategoryRepository, ExpenseCategoryRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IBudgetingUnitOfWork, BudgetingUnitOfWork>();
        services.AddScoped<ExpenseCategoryMembershipPolicy>();

        // ADR-0002 §4: an operation type in a category and a currency of a budget count as used.
        services.AddScoped<IReferenceItemUsageProbe, BudgetingReferenceItemUsageProbe>();

        return services;
    }
}
