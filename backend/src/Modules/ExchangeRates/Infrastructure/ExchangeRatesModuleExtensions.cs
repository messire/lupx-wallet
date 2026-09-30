using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Composition point for the ExchangeRates module (schema "exchange_rates") — called from
/// Host/Program.cs. Registers the DbContext, repositories, Frankfurter client, the
/// auto-refresh background job (ADR-0004) and the PrimaryWalletChanged subscriber
/// (ADR-0004 p.5, ADR-0004) — the latter is registered manually (not via
/// MediatR.RegisterServicesFromAssembly, which in Program.cs scans only *.Application
/// assemblies, while the handler and RefreshExchangeRatesCommand's command handler live in
/// Infrastructure, see RefreshExchangeRatesCommandHandler.cs).
/// </summary>
public static class ExchangeRatesModuleExtensions
{
    public static IServiceCollection AddExchangeRatesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<ExchangeRatesDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "exchange_rates"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<ILatestExchangeRateRepository, LatestExchangeRateRepository>();
        services.AddScoped<IHistoricalExchangeRateRepository, HistoricalExchangeRateRepository>();
        services.AddScoped<IExchangeRatesUnitOfWork, ExchangeRatesUnitOfWork>();
        services.AddScoped<IExchangeRateLookup, ExchangeRateLookup>();

        // Клиент Frankfurter (ADR-0004) — FrankfurterTestableHandler всегда делегирует
        // реальному SocketsHttpHandler в production/разработке; интеграционные тесты
        // подставляют TestOverride через тот же Singleton-экземпляр (без реального сетевого
        // вызова, docs/PROGRESS.md, W1.1 DoD).
        services.AddSingleton<FrankfurterTestableHandler>();
        services
            .AddHttpClient(FrankfurterClient.HttpClientName, client =>
            {
                var baseUrl = configuration["ExchangeRates:FrankfurterBaseUrl"] ?? "https://api.frankfurter.dev/v2/";
                client.BaseAddress = NormalizeBaseAddress(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10);
            })
            .ConfigurePrimaryHttpMessageHandler(provider => provider.GetRequiredService<FrankfurterTestableHandler>());
        services.AddScoped<IFrankfurterClient, FrankfurterClient>();

        services.AddSingleton<ExchangeRateRefreshSignal>();
        services.AddSingleton<IExchangeRateRefreshSignal>(provider => provider.GetRequiredService<ExchangeRateRefreshSignal>());

        // RefreshExchangeRatesCommandHandler живёт в Infrastructure (не найдётся сканированием
        // *.Application-сборок в Program.cs) — регистрируется вручную, как и подписчик ниже.
        services.AddScoped<IRequestHandler<RefreshExchangeRatesCommand, RefreshExchangeRatesResult>, RefreshExchangeRatesCommandHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PrimaryWalletChanged>>, PrimaryWalletChangedHandler>();

        services.AddHostedService<ExchangeRateRefreshBackgroundService>();

        // ADR-0002, §4: одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe.
        services.AddScoped<IReferenceItemUsageProbe, ExchangeRatesReferenceItemUsageProbe>();

        return services;
    }

    /// <summary>
    /// Ensures the trailing slash: without it <c>new Uri(base, "rates?...")</c> drops the last
    /// path segment (".../v2" + "rates" → ".../rates").
    /// </summary>
    public static Uri NormalizeBaseAddress(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        return new Uri(trimmed.EndsWith('/') ? trimmed : trimmed + "/");
    }
}
