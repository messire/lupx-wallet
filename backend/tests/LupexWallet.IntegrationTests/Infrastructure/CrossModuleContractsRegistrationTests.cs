using FluentAssertions;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// ADR-0009, раздел «Обязательное условие: пустой список реализаций — ошибка, не «false»».
///
/// Каждый порт, реализуемый несколькими модулями-владельцами через <c>IEnumerable&lt;TPort&gt;</c>,
/// фиксирует ОЖИДАЕМОЕ число зарегистрированных реализаций (известное на этапе компоновки Host).
/// Проверка `Count == 0` в самих обработчиках (см. <see cref="IWalletHistorySource"/>,
/// <see cref="IReferenceItemUsageProbe"/>) ловит только случай «забыли зарегистрировать ВСЕ»
/// реализации — если забыть зарегистрировать ОДНУ из нескольких (например, `AddOperationsModule`
/// перестанет регистрировать `OperationsWalletHistorySource`, а `BalanceHistoryWalletHistorySource`
/// останется), список останется непустым и проверка `Count == 0` не сработает: конфигурационная
/// ошибка тихо приведёт к неполной агрегации (`hasHistory`/`isUsed` могут вернуть `false`,
/// хотя фактически история/использование есть).
///
/// Этот тест — регрессионный guard на количество регистраций в композиции Host: если кто-то
/// уберёт регистрацию одной из реализаций, тест упадёт с понятным сообщением о несовпадении
/// количества вместо тихого пропуска проблемы.
/// </summary>
public sealed class CrossModuleContractsRegistrationTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// Случай (a) ADR-0009: <see cref="IWalletHistorySource"/> — ровно 2 реализации-владельца:
    /// <c>Operations.Infrastructure.OperationsWalletHistorySource</c> и
    /// <c>BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource</c>.
    /// </summary>
    [Fact]
    public void WalletHistorySource_HasExactlyExpectedNumberOfRegisteredImplementations()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IWalletHistorySource>();

        sources.Should().HaveCount(2,
            "ADR-0009 (случай a) ожидает ровно 2 владельца данных истории кошелька " +
            "(Operations, BalanceHistory) — если их число изменилось, кто-то забыл " +
            "зарегистрировать реализацию в Add<Owner>Module либо ожидание в этом тесте " +
            "устарело и должно быть обновлено осознанно вместе с ADR-0009");
    }

    /// <summary>
    /// Случай (c) ADR-0009: <see cref="IReferenceItemUsageProbe"/> — ровно 3 реализации-владельца:
    /// <c>Wallets.Infrastructure.WalletsReferenceItemUsageProbe</c>,
    /// <c>Operations.Infrastructure.OperationsReferenceItemUsageProbe</c>,
    /// <c>ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe</c>.
    /// </summary>
    [Fact]
    public void ReferenceItemUsageProbe_HasExactlyExpectedNumberOfRegisteredImplementations()
    {
        using var scope = fixture.Factory.Services.CreateScope();

        var probes = scope.ServiceProvider.GetServices<IReferenceItemUsageProbe>();

        probes.Should().HaveCount(3,
            "ADR-0009 (случай c) ожидает ровно 3 владельца данных использования элементов " +
            "справочника (Wallets, Operations, ExchangeRates) — если их число изменилось, " +
            "кто-то забыл зарегистрировать пробник в Add<Owner>Module либо ожидание в этом " +
            "тесте устарело и должно быть обновлено осознанно вместе с ADR-0009");
    }
}
