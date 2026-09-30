using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// ADR-0001, §2, «Обязательное условие: пустой список реализаций — ошибка, не «false»».
///
/// Каждый порт, реализуемый модулями-владельцами через <c>IEnumerable&lt;TPort&gt;</c>,
/// фиксирует ОЖИДАЕМОЕ число зарегистрированных реализаций (известное на этапе компоновки Host).
/// Проверка `Count == 0` в самих обработчиках (см. <see cref="IWalletHistorySource"/>,
/// <see cref="IReferenceItemUsageProbe"/>) ловит только случай «забыли зарегистрировать ВСЕ»
/// реализации — если забыть зарегистрировать ОДНУ из нескольких (например, `AddWalletsModule`
/// перестанет регистрировать `WalletsReferenceItemUsageProbe`, а `OperationsReferenceItemUsageProbe`
/// останется), список останется непустым и проверка `Count == 0` не сработает: конфигурационная
/// ошибка тихо приведёт к неполной агрегации (`isUsed` может вернуть `false`, хотя
/// фактически использование есть).
///
/// Этот тест — регрессионный guard на количество регистраций в композиции Host: если кто-то
/// добавит или уберёт реализацию, тест упадёт с понятным сообщением о несовпадении
/// количества вместо тихого пропуска проблемы.
/// </summary>
public sealed class CrossModuleContractsRegistrationTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// ADR-0002, §2: <see cref="IWalletHistorySource"/> — ровно 1 реализация, владелец данных истории
    /// (историей считаются только операции, слепки — нет):
    /// <c>Operations.Infrastructure.OperationsWalletHistorySource</c>.
    /// </summary>
    [Fact]
    public void WalletHistorySource_HasExactlyExpectedNumberOfRegisteredImplementations()
    {
        using var scope = Fixture.Factory.Services.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IWalletHistorySource>();

        sources.Should().HaveCount(1,
            "ADR-0002 §2 ожидает ровно 1 владельца данных истории кошелька (Operations; " +
            "слепки BalanceHistory историей не считаются) — если число изменилось, кто-то " +
            "зарегистрировал лишнюю реализацию либо забыл зарегистрировать OperationsWalletHistorySource " +
            "в AddOperationsModule, либо ожидание в этом тесте устарело и должно быть обновлено " +
            "осознанно вместе с ADR-0002");
    }

    /// <summary>
    /// ADR-0002, §4: <see cref="IReferenceItemUsageProbe"/> — ровно 2 реализации-владельца:
    /// <c>Wallets.Infrastructure.WalletsReferenceItemUsageProbe</c> и
    /// <c>Operations.Infrastructure.OperationsReferenceItemUsageProbe</c> (курсы валют использованием не считаются).
    /// </summary>
    [Fact]
    public void ReferenceItemUsageProbe_HasExactlyExpectedNumberOfRegisteredImplementations()
    {
        using var scope = Fixture.Factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<IReferenceItemUsageProbe>();

        probes.Should().HaveCount(2,
            "ADR-0002 §4 ожидает ровно 2 владельца данных использования элементов справочника " +
            "(Wallets, Operations; курсы ExchangeRates использованием не считаются) — если число " +
            "изменилось, кто-то зарегистрировал лишний пробник либо забыл зарегистрировать " +
            "пробник в Add<Owner>Module, либо ожидание в этом тесте устарело и должно быть " +
            "обновлено осознанно вместе с ADR-0002");
    }
}
