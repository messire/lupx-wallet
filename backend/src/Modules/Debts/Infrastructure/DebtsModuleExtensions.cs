using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Debts.Application;
using LupexWallet.ReferenceData.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Debts.Infrastructure;

/// <summary>Composition root of the Debts module (schema "debts", ADR-0010 §3) — called from Host/Program.cs.</summary>
public static class DebtsModuleExtensions
{
    public static IServiceCollection AddDebtsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<DebtsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "debts"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<ICreditorRepository, CreditorRepository>();
        services.AddScoped<IDebtRepository, DebtRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IDebtsUnitOfWork, DebtsUnitOfWork>();
        services.AddScoped<PaymentFactsReader>();
        services.AddScoped<ExpensePaymentRecorder>();
        services.AddScoped<DebtsReferenceValidator>();

        // ADR-0002 §4: currencies referenced by debts/loans are in use.
        services.AddScoped<IReferenceItemUsageProbe, DebtsReferenceItemUsageProbe>();

        return services;
    }
}
