using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>
/// Точка композиции модуля Wallets (схема "wallets") — вызывается из Host/Program.cs.
/// Регистрирует DbContext, репозиторий, доменные сервисы и обработчики команд/запросов.
/// </summary>
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

        return services;
    }
}
