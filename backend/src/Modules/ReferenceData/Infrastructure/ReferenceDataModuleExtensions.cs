using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ReferenceData.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Composition root for the ReferenceData module (schema "reference_data") — called from
/// Host/Program.cs. Registers the DbContext, repositories, and command/query handlers.
/// </summary>
public static class ReferenceDataModuleExtensions
{
    public static IServiceCollection AddReferenceDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LupexWallet")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:LupexWallet — требуется всем модулям (ADR-0001: одна база, схема на модуль).");

        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<ReferenceDataDbContext>((provider, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "reference_data"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IWalletTypeRepository, WalletTypeRepository>();
        services.AddScoped<IOperationTypeRepository, OperationTypeRepository>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<IAvailableCurrencyRepository, AvailableCurrencyRepository>();
        services.AddScoped<CurrencyActivationPolicy>();
        services.AddScoped<IOperationBehaviorKindRepository, OperationBehaviorKindRepository>();
        services.AddScoped<IReferenceDataUnitOfWork, ReferenceDataUnitOfWork>();
        services.AddScoped<IOperationTypeLookup, OperationTypeLookup>();
        services.AddScoped<ICurrencyLookup, CurrencyLookup>();

        // Ports published for other modules (ADR-0001 §2, way c).
        services.AddScoped<IAvailableCurrencyCatalog, AvailableCurrencyCatalog>();
        services.AddScoped<ICurrencyActivation, CurrencyActivation>();

        // Wallet type resolution for the Wallets create/update commands.
        services.AddScoped<IWalletTypeLookup, WalletTypeLookup>();

        return services;
    }
}
