using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Funds.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>Composition root of the Funds module (schema "funds", ADR-0010 §4) — called from Host/Program.cs.</summary>
public static class FundsModuleExtensions
{
    public static IServiceCollection AddFundsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<FundsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "funds"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IFundRepository, FundRepository>();
        services.AddScoped<IFundsUnitOfWork, FundsUnitOfWork>();
        services.AddScoped<ReservationPolicy>();
        services.AddScoped<FundViewBuilder>();
        services.AddScoped<IFundReservationsLookup, FundReservationsLookup>();

        // ADR-0010 §4: the only implementation of Wallets.Application.IWalletReservationProbe.
        services.AddScoped<IWalletReservationProbe, FundsWalletReservationProbe>();

        // ADR-0002 §4: one of the implementations of ReferenceData.Application.IReferenceItemUsageProbe.
        services.AddScoped<IReferenceItemUsageProbe, FundsReferenceItemUsageProbe>();

        return services;
    }
}
