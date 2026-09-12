using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>Composition root of the Wallets module (schema "wallets") — called from Host/Program.cs. Registers the DbContext, repository, domain services and command/query handlers.</summary>
public static class WalletsModuleExtensions
{
    public static IServiceCollection AddWalletsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0006: одна база, схема на модуль).");

        // Интерцептор регистрируется в DI и резолвится через (provider, options) — не через
        // services.BuildServiceProvider() (антипаттерн: создаёт отдельный root-провайдер,
        // не совпадающий со scope запроса).
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<WalletsDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "wallets"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IWalletsUnitOfWork, WalletsUnitOfWork>();
        services.AddScoped<PrimaryWalletPolicy>();
        services.AddScoped<IWalletBalanceGateway, WalletBalanceGateway>();
        services.AddScoped<IWalletDirectory, WalletDirectory>();
        services.AddScoped<IWalletCurrencySet, WalletCurrencySet>();
        services.AddScoped<IWalletTotalsSource, WalletTotalsSource>();

        // ADR-0009, случай c: одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe.
        services.AddScoped<IReferenceItemUsageProbe, WalletsReferenceItemUsageProbe>();

        return services;
    }
}
