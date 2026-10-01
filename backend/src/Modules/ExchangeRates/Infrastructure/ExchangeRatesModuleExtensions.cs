using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Composition point for the ExchangeRates module (schema "exchange_rates") — called from
/// Host/Program.cs. Registers the DbContext, repositories, Frankfurter client, the
/// background job (rates refresh + history load, ADR-0004) and the event subscribers
/// (PrimaryWalletChanged, WalletCreated, WalletCurrencyChanged, CurrencyCreated, CurrencyDeleted).
/// Subscribers and the command handlers of Refresh/Backfill are registered manually (not via
/// MediatR.RegisterServicesFromAssembly, which in Program.cs scans only *.Application
/// assemblies, while they live in Infrastructure, see RefreshExchangeRatesCommandHandler.cs).
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
        services.AddScoped<IHistoricalRateCoverageRepository, HistoricalRateCoverageRepository>();
        services.AddScoped<IExchangeRatesUnitOfWork, ExchangeRatesUnitOfWork>();
        services.AddScoped<IExchangeRateLookup, ExchangeRateLookup>();

        // Клиент Frankfurter (ADR-0004) — FrankfurterTestableHandler всегда делегирует
        // реальному SocketsHttpHandler в production/разработке; интеграционные тесты
        // подставляют TestOverride через тот же Singleton-экземпляр (без реального сетевого
        // вызова).
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

        services.AddOptions<ExchangeRateHistoryOptions>()
            .Bind(configuration.GetSection(ExchangeRateHistoryOptions.SectionName))
            .Validate(o => o.HistoryChunkDays >= 1, "ExchangeRates:HistoryChunkDays must be an integer not less than 1.")
            .ValidateOnStart();

        services.AddSingleton<ExchangeRateRefreshSignal>();
        services.AddSingleton<IExchangeRateRefreshSignal>(provider => provider.GetRequiredService<ExchangeRateRefreshSignal>());

        // RefreshExchangeRatesCommandHandler живёт в Infrastructure (не найдётся сканированием
        // *.Application-сборок в Program.cs) — регистрируется вручную, как и подписчик ниже.
        services.AddScoped<IRequestHandler<RefreshExchangeRatesCommand, RefreshExchangeRatesResult>, RefreshExchangeRatesCommandHandler>();
        services.AddScoped<IRequestHandler<BackfillExchangeRateHistoryCommand, BackfillResult>, BackfillExchangeRateHistoryCommandHandler>();
        // ADR-0004 §5, §9: each of these means "something to catch up on" — the subscribers only signal.
        services.AddScoped<INotificationHandler<DomainEventNotification<PrimaryWalletChanged>>, PrimaryWalletChangedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCreated>>, WalletCreatedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>, WalletCurrencyChangedHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<CurrencyCreated>>, CurrencyCreatedHandler>();
        // ADR-0002 §4: rates are not "usage" of a currency — they are deleted together with it.
        services.AddScoped<INotificationHandler<DomainEventNotification<CurrencyDeleted>>, CurrencyDeletedHandler>();

        services.AddHostedService<ExchangeRateRefreshBackgroundService>();

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
