using LupexWallet.Audit.Infrastructure;
using LupexWallet.BalanceHistory.Infrastructure;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.ReferenceData.Infrastructure;
using LupexWallet.Wallets.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// WebApplicationFactory поверх реального Host (Program.cs) — подменяет
/// ConnectionStrings:LupexWallet на контейнер Testcontainers, остальная композиция
/// (DI, MediatR pipeline, аутентификация) — как в продакшене (Environment = Development,
/// поэтому используется тестовый пароль из appsettings.Development.json — README.md).
///
/// Строка подключения задаётся через переменную окружения ДО обращения к <see cref="WebApplicationFactory{TEntryPoint}.Services"/>
/// (конструктор, не ConfigureWebHost/ConfigureAppConfiguration): каждый Add&lt;Module&gt;Module(...) в
/// Program.cs читает IConfiguration.GetConnectionString("LupexWallet") один раз при регистрации DbContext
/// и замыкает значение в лямбду — конфигурация, добавленная позже через ConfigureAppConfiguration,
/// на уже построенный provider не влияет.
/// </summary>
public sealed class LupexWalletApiFactory : WebApplicationFactory<Program>
{
    public LupexWalletApiFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__LupexWallet", connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Фоновые IHostedService (BalanceSnapshotSchedulerHostedService,
        // ExchangeRateRefreshBackgroundService) работают в собственном DI-scope, независимом
        // от HTTP-запроса теста, и могут выполнять запросы к БД в произвольный момент —
        // в частности, ExchangeRateRefreshBackgroundService просыпается по сигналу на КАЖДОЕ
        // создание первого/основного кошелька (ADR-0001 п.5), что в тестах происходит в
        // каждом тесте после Respawn-сброса. Без отключения это гонится с
        // ApiTestFixture.ResetAsync() (Respawn TRUNCATE) между тестами и может привести к
        // 40P01 deadlock detected на стороне PostgreSQL. Бизнес-логика самих фоновых задач
        // покрыта отдельно unit-тестами планировщиков (BalanceSnapshotSchedulerHostedService.
        // NextRunDelay, ExchangeRateRefreshTimer) и синхронными путями (каскадный пересчет
        // BalanceHistory при CRUD операций) — эти интеграционные тесты не нуждаются в реально
        // работающих фоновых сервисах.
        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }

    /// <summary>
    /// Применяет EF Core-миграции всех модулей (каждый — своя схема, ADR-0006).
    /// Вызывать один раз на контейнер (ApiTestFixture), не на каждый тест.
    /// Модули без своих таблиц (Reporting) сюда добавляются по мере появления в них домена/миграций.
    /// </summary>
    public async Task MigrateAllAsync()
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;

        await sp.GetRequiredService<WalletsDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<ReferenceDataDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<OperationsDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<BalanceHistoryDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<ExchangeRatesDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<AuditDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// Единственная точка подмены реального HTTP-вызова к Frankfurter в интеграционных
    /// тестах (docs/PROGRESS.md, W1.1 DoD) — тот же Singleton-экземпляр, что использует
    /// именованный HttpClient "Frankfurter" внутри приложения (ExchangeRatesModuleExtensions).
    /// </summary>
    public FrankfurterTestableHandler GetFrankfurterTestableHandler() =>
        Services.GetRequiredService<FrankfurterTestableHandler>();
}
